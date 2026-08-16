import { signChallenge } from '../lib/clientKey.ts'

/**
 * Womit sich die App beim Agent ausweist.
 *
 * Zwei Wege, dieselbe Schnittstelle: das alte geteilte Token liegt sofort vor,
 * ein Sitzungstoken muss erst per Challenge-Response geholt werden. Der
 * Transport merkt den Unterschied nur daran, ob {@link Credentials.peek} schon
 * etwas liefert.
 */
export interface Credentials {
  /** Was bereits vorliegt — `undefined` heißt: muss erst geholt werden. */
  peek(): string | undefined
  /** Besorgt ein gültiges Token; gleichzeitige Aufrufe teilen sich einen Vorgang. */
  obtain(): Promise<string>
  /** Verwirft das gemerkte Token, etwa nach einem 401. */
  invalidate(): void
}

/** Der alte Weg: ein Token, das schon dasteht. */
export function staticCredentials(token: string): Credentials {
  return {
    peek: () => token,
    obtain: () => Promise.resolve(token),
    invalidate: () => {
      // Ein Pre-Shared-Token wird nicht ungültig — es ist entweder richtig
      // oder war es nie.
    },
  }
}

/** Die beiden Aufrufe, mit denen sich ein gekoppelter Client anmeldet. */
export interface SessionExchange {
  /** Holt die Challenge des Agents. */
  challenge(clientId: string): Promise<string>
  /** Legt die Unterschrift vor und bekommt das Sitzungstoken. */
  open(clientId: string, nonce: string, signature: string): Promise<string>
}

/**
 * Der gekoppelte Weg: Challenge holen, mit dem eigenen Schlüssel unterschreiben,
 * Sitzungstoken bekommen.
 *
 * Das Token wird gemerkt, solange es gilt. Neu geholt wird es erst, wenn der
 * Agent es ablehnt — eine Uhr auf der Client-Seite wäre nur eine zweite Quelle
 * für dieselbe Wahrheit, und sie geht garantiert anders als die des Agents.
 */
export function pairedCredentials(
  clientId: string,
  /**
   * Der private Schlüssel — als Frage und nicht als Wert.
   *
   * **Der Befund dahinter (16.08.2026):** er wurde vorher **synchron** aus dem
   * Speicher der App gelesen, bevor überhaupt feststand, ob dort einer liegt.
   * Seit 31h liegt er dort in aller Regel nicht: er gehört der Gegenstelle
   * dieses Geräts (`clientkey.txt` bzw. `clientkey.json`), und die antwortet
   * nur asynchron. Der Aufrufer fiel deshalb auf ein leeres Token zurück, und
   * jede Anfrage ging ohne Berechtigung hinaus.
   */
  privateKey: () => Promise<string>,
  exchange: SessionExchange,
): Credentials {
  let token: string | undefined
  let pending: Promise<string> | undefined

  return {
    peek: () => token,

    obtain: async (): Promise<string> => {
      if (token !== undefined) {
        return token
      }

      // Ohne dieses Zusammenfassen würden Bild, Eingabe und die erste Abfrage
      // beim Start drei Anmeldungen gleichzeitig auslösen.
      pending ??= (async (): Promise<string> => {
        const nonce = await exchange.challenge(clientId)
        const fresh = await exchange.open(
          clientId,
          nonce,
          await signChallenge(await privateKey(), nonce),
        )

        token = fresh
        return fresh
      })().finally(() => {
        pending = undefined
      })

      return await pending
    },

    invalidate: () => {
      token = undefined
    },
  }
}
