import { signChallenge } from '../lib/clientKey.ts'

/**
 * Womit sich die App beim Agent ausweist: ein Sitzungstoken, geholt per
 * Challenge-Response mit dem Schlüssel aus der Kopplung. Der Transport merkt
 * nur daran, ob {@link Credentials.peek} schon etwas liefert, ob er die
 * Anmeldung noch abwarten muss.
 *
 * Das alte geteilte Token aus der Zeit vor der Kopplung gibt es seit v1.4
 * nicht mehr — ein Geheimnis, das für alles gilt und keine Rechte kennt.
 */
export interface Credentials {
  /** Was bereits vorliegt — `undefined` heißt: muss erst geholt werden. */
  peek(): string | undefined
  /** Besorgt ein gültiges Token; gleichzeitige Aufrufe teilen sich einen Vorgang. */
  obtain(): Promise<string>
  /** Verwirft das gemerkte Token, etwa nach einem 401. */
  invalidate(): void
}

/**
 * Ein Gerät ohne Kopplung hat keinen Ausweis. Jede Anfrage scheitert dann mit
 * einem Satz, der das sagt — statt mit einem leeren Token und einem 401, das
 * wie ein Fehler des Agents aussieht.
 */
export function noCredentials(): Credentials {
  return {
    peek: () => undefined,
    obtain: () => Promise.reject(new Error('Dieses Gerät ist nicht gekoppelt.')),
    invalidate: () => {},
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
