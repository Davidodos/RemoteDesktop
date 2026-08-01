import { createPublicKey, randomBytes, randomInt, verify } from 'node:crypto'
import { ClientStore, fingerprintOf, type PairedClient } from './clients.js'

/** Wie lange ein Kopplungscode gilt. Kurz genug, dass Raten nichts bringt. */
export const CODE_LIFETIME_MS = 5 * 60 * 1000

/** Wie lange eine Challenge gilt — sie wird sofort danach unterschrieben. */
export const CHALLENGE_LIFETIME_MS = 60 * 1000

/** Wie lange ein Sitzungstoken gilt. Danach meldet sich der Client neu an. */
export const SESSION_LIFETIME_MS = 12 * 60 * 60 * 1000

const MAX_LABEL_LENGTH = 64

export type PairOutcome = 'ok' | 'bad-code' | 'bad-label' | 'bad-key'
export type SessionOutcome = 'ok' | 'unknown-client' | 'bad-challenge' | 'bad-signature'

/**
 * Kopplung und Anmeldung des Wakers — derselbe Ablauf wie am Agent
 * (`agent/Auth/PairingService.cs`), nur in Node.
 *
 * Er ist bewusst identisch: die App unterschreibt mit demselben Schlüssel und
 * denselben Aufrufen, egal ob am anderen Ende ein PC oder die NAS steht. Ein
 * Waker ist damit für die App ein gekoppeltes Gerät wie ein Agent — nur eines,
 * das außer Wecken nichts kann.
 *
 * Ohne diese Absicherung wäre der Waker ein offener Broadcast-Sender im Netz.
 * Der Schaden bliebe klein — WOL kann nur einschalten —, aber stehenlassen
 * würde man das trotzdem nicht.
 */
export class PairingService {
  private code: { value: string; expiresAt: number } | undefined
  private readonly challenges = new Map<string, { nonce: string; expiresAt: number }>()
  private readonly sessions = new Map<string, { clientId: string; expiresAt: number }>()

  constructor(
    private readonly clients: ClientStore,
    private readonly now: () => number = Date.now,
  ) {}

  /**
   * Erzeugt einen sechsstelligen Kopplungscode. Es gibt immer nur einen: ein
   * zweiter Aufruf verwirft den vorigen, damit ein vergessener Code nicht
   * fünf Minuten lang gültig herumliegt.
   */
  issueCode(): string {
    const value = String(randomInt(0, 1_000_000)).padStart(6, '0')

    this.code = { value, expiresAt: this.now() + CODE_LIFETIME_MS }

    return value
  }

  /**
   * Nimmt einen Client auf, der den angezeigten Code richtig eingetippt hat.
   *
   * Der Code wird zuerst geprüft und dabei verbraucht. Wer ihn errät, soll
   * nicht dadurch einen zweiten Versuch bekommen, dass sein Schlüssel
   * unbrauchbar war.
   */
  pair(
    code: string,
    label: string,
    publicKey: string,
  ): { outcome: PairOutcome; client?: PairedClient } {
    if (!this.redeem(code)) {
      return { outcome: 'bad-code' }
    }

    const trimmed = label.trim()

    if (trimmed.length === 0 || trimmed.length > MAX_LABEL_LENGTH) {
      return { outcome: 'bad-label' }
    }

    if (!isUsablePublicKey(publicKey)) {
      return { outcome: 'bad-key' }
    }

    const timestamp = new Date(this.now()).toISOString()

    const client: PairedClient = {
      id: fingerprintOf(publicKey),
      label: trimmed,
      publicKey,
      createdAt: timestamp,
      lastSeenAt: timestamp,
    }

    this.clients.add(client)

    return { outcome: 'ok', client }
  }

  /** Die Challenge, oder `undefined` bei unbekanntem Client. */
  challenge(clientId: string): string | undefined {
    if (this.clients.find(clientId) === undefined) {
      return undefined
    }

    const nonce = randomBytes(32).toString('base64')

    this.challenges.set(clientId, { nonce, expiresAt: this.now() + CHALLENGE_LIFETIME_MS })

    return nonce
  }

  /** Prüft die Unterschrift über die Challenge und öffnet bei Erfolg eine Sitzung. */
  openSession(
    clientId: string,
    nonce: string,
    signature: string,
  ): { outcome: SessionOutcome; token?: string } {
    const client = this.clients.find(clientId)

    if (client === undefined) {
      return { outcome: 'unknown-client' }
    }

    const pending = this.challenges.get(clientId)

    // Eine Challenge gilt genau einmal. Sonst ließe sich eine einmal
    // abgehörte Unterschrift beliebig oft wiederverwenden.
    this.challenges.delete(clientId)

    if (pending === undefined || pending.nonce !== nonce || pending.expiresAt < this.now()) {
      return { outcome: 'bad-challenge' }
    }

    if (!verifySignature(client.publicKey, nonce, signature)) {
      return { outcome: 'bad-signature' }
    }

    const token = randomBytes(32).toString('base64url')

    this.sessions.set(token, { clientId, expiresAt: this.now() + SESSION_LIFETIME_MS })
    this.clients.touch(clientId, new Date(this.now()).toISOString())

    return { outcome: 'ok', token }
  }

  /** Die Kennung hinter einem Sitzungstoken, oder `undefined`. */
  resolveSession(token: string | undefined): string | undefined {
    if (token === undefined) {
      return undefined
    }

    const session = this.sessions.get(token)

    if (session === undefined) {
      return undefined
    }

    if (session.expiresAt < this.now()) {
      this.sessions.delete(token)
      return undefined
    }

    return session.clientId
  }

  /**
   * Widerruft einen Client und wirft ihn zugleich aus seinen laufenden
   * Sitzungen. Beides gehört zusammen — den Eintrag allein zu löschen
   * verschöbe die Wirkung um bis zu zwölf Stunden.
   */
  revoke(clientId: string): boolean {
    for (const [token, session] of this.sessions) {
      if (session.clientId === clientId) {
        this.sessions.delete(token)
      }
    }

    return this.clients.revoke(clientId)
  }

  private redeem(code: string): boolean {
    const pending = this.code

    // Auch ein falscher Versuch verbraucht nichts, aber ein richtiger schon:
    // ein Code funktioniert kein zweites Mal.
    if (pending === undefined || pending.expiresAt < this.now() || pending.value !== code) {
      return false
    }

    this.code = undefined
    return true
  }
}

/**
 * Prüft die Unterschrift eines Clients über eine Challenge.
 *
 * Erwartet wird das Format, das die WebCrypto-API des Browsers liefert: r und s
 * hintereinander mit fester Länge (IEEE P1363), **nicht** DER. Wer hier das
 * falsche Format annimmt, bekommt eine Prüfung, die immer fehlschlägt.
 */
export function verifySignature(
  publicKeyBase64: string,
  nonceBase64: string,
  signatureBase64: string,
): boolean {
  try {
    const key = createPublicKey({
      key: Buffer.from(publicKeyBase64, 'base64'),
      format: 'der',
      type: 'spki',
    })

    return verify(
      'sha256',
      Buffer.from(nonceBase64, 'base64'),
      { key, dsaEncoding: 'ieee-p1363' },
      Buffer.from(signatureBase64, 'base64'),
    )
  } catch {
    // Ein unbrauchbarer Schlüssel oder eine unbrauchbare Unterschrift sind kein
    // Sonderfall, sondern einfach „nicht bestanden".
    return false
  }
}

/** Nur P-256 wird angenommen — eine andere Kurve hat hier nie jemand getestet. */
function isUsablePublicKey(publicKeyBase64: string): boolean {
  try {
    const key = createPublicKey({
      key: Buffer.from(publicKeyBase64, 'base64'),
      format: 'der',
      type: 'spki',
    })

    return key.asymmetricKeyType === 'ec' && key.asymmetricKeyDetails?.namedCurve === 'prime256v1'
  } catch {
    return false
  }
}
