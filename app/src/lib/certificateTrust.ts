/**
 * Das selbst ausgestellte Zertifikat eines Agents holen und prüfen.
 *
 * **Warum es das gibt:** ohne Tailscale gibt es keine öffentliche Stelle, die
 * ein Zertifikat für `192.168.178.20` ausstellen würde. Der Agent stellt sich
 * deshalb selbst eins aus (siehe `agent/Services/SelfSignedCertificate.cs`) —
 * und dieses Gerät muss der zugehörigen Stelle einmal vertrauen, sonst
 * scheitert jede Verbindung, noch bevor ein Token geprüft wird.
 *
 * **Warum das sicher ist, obwohl die Datei unverschlüsselt kommt:** sie *muss*
 * unverschlüsselt kommen — über eine Verbindung, der man noch nicht traut,
 * lässt sich nichts holen. Sie ist aber öffentlich und enthält kein Geheimnis.
 * Was sie echt macht, ist der Fingerabdruck, und der kam über die Kopplung:
 * über den Code auf dem Bildschirm des Rechners, nicht über das Netz. Passt er
 * nicht, wird nichts installiert.
 */

/** Der Port, auf dem der Agent ausschließlich sein CA-Zertifikat anbietet. */
export const TRUST_PORT = 8442

export class TrustError extends Error {}

/**
 * Der Fingerabdruck einer eigenen Zertifizierungsstelle — oder `undefined`,
 * wenn da keiner steht.
 *
 * **Der Befund dahinter:** der Agent schreibt bei einem Zertifikat von Tailscale
 * ausdrücklich `"caFingerprint": null` in seine Antwort. Geprüft wurde aber auf
 * `=== undefined`, und `null` ist nicht `undefined` — also übernahm die App
 * `caFingerprint: null` in das Gerät und hielt jeden Rechner für einen mit
 * selbst ausgestelltem Zertifikat.
 *
 * Am echten Gerät sah das so aus: nach dem Koppeln kam die Rückfrage
 * „Zertifikat bestätigen", der Knopf tat nichts (es gab ja keinen
 * Fingerabdruck zu vergleichen), und „Später" verband sofort und ohne
 * Beanstandung — weil das Zertifikat längst in Ordnung war. Ein Schritt, den
 * man überspringen kann und danach nie wieder sieht, ist genau die Art Fehler,
 * die niemand meldet und die trotzdem jedes Koppeln verdirbt.
 *
 * Geprüft wird deshalb der Wert selbst: 64 Hexzeichen, sonst nichts.
 */
export function certificateFingerprint(value: unknown): string | undefined {
  if (typeof value !== 'string') {
    return undefined
  }

  const trimmed = value.trim().toLowerCase()

  return /^[0-9a-f]{64}$/.test(trimmed) ? trimmed : undefined
}

export interface AgentCertificate {
  /** Das Zertifikat selbst, als Base64 — so nimmt es die Android-Seite entgegen. */
  base64: string
  /** Der geprüfte Fingerabdruck, kleingeschrieben und ohne Trennzeichen. */
  fingerprint: string
}

/**
 * Wo das Zertifikat liegt. Bewusst `http`: ein `https` an dieser Stelle wäre
 * genau die Verbindung, die noch nicht zustande kommt.
 */
export function certificateUrl(host: string, port = TRUST_PORT): string {
  return `http://${host}:${port}/ca.crt`
}

/**
 * Holt das Zertifikat und gibt es nur heraus, wenn sein Fingerabdruck stimmt.
 *
 * @param expected Der Fingerabdruck aus der Kopplung. Ohne ihn wird nichts
 *   geholt — ein Zertifikat ohne Vergleichswert anzunehmen wäre dasselbe wie
 *   gar nicht zu prüfen.
 */
export async function fetchAgentCertificate(
  host: string,
  expected: string,
  fetcher: typeof fetch = fetch,
  port = TRUST_PORT,
): Promise<AgentCertificate> {
  const wanted = expected.trim().toLowerCase()

  if (!/^[0-9a-f]{64}$/.test(wanted)) {
    throw new TrustError('Ohne Fingerabdruck aus der Kopplung wird nichts bestätigt.')
  }

  const found = await downloadAuthority(host, fetcher, port)

  if (found.fingerprint !== wanted) {
    // Der eine Fall, der wirklich zählt: hier säße ein Angreifer im Netz, der
    // sein eigenes Zertifikat unterschiebt.
    throw new TrustError(
      'Das Zertifikat gehört nicht zu diesem Rechner. Nicht bestätigen — ' +
        'im Netz sitzt jemand dazwischen, oder es ist der falsche Rechner.',
    )
  }

  return found
}

/**
 * Holt das Zertifikat **ohne** Vergleichswert und gibt seinen Fingerabdruck
 * heraus.
 *
 * Für den Weg ohne QR-Code: am PC sitzt keine Kamera, also tippt jemand Adresse
 * und Code ab, und der Fingerabdruck kann nicht mitkommen. Dann übernimmt das
 * Auge die Rolle der Kamera — die Gegenstelle zeigt ihn auf ihrem Bildschirm
 * an, und beide werden nebeneinandergelegt. Derselbe Anker, nur langsamer.
 *
 * Was hier herauskommt, darf deshalb **nie** ohne Rückfrage installiert
 * werden.
 */
export async function downloadAuthority(
  host: string,
  fetcher: typeof fetch = fetch,
  port = TRUST_PORT,
): Promise<AgentCertificate> {
  let response: Response

  try {
    response = await fetcher(certificateUrl(host, port))
  } catch (cause) {
    throw new TrustError(
      `Der Rechner antwortet auf Port ${port} nicht. Läuft der Agent, und ist der Port frei?`,
      { cause },
    )
  }

  if (!response.ok) {
    throw new TrustError(`Der Rechner hat kein Zertifikat geliefert (HTTP ${response.status}).`)
  }

  const raw = new Uint8Array(await response.arrayBuffer())

  if (raw.byteLength === 0) {
    throw new TrustError('Der Rechner hat eine leere Datei geliefert.')
  }

  return { base64: toBase64(raw), fingerprint: await sha256(raw) }
}

async function sha256(data: Uint8Array): Promise<string> {
  const digest = await crypto.subtle.digest('SHA-256', data as unknown as BufferSource)

  return [...new Uint8Array(digest)].map((byte) => byte.toString(16).padStart(2, '0')).join('')
}

function toBase64(data: Uint8Array): string {
  let binary = ''

  for (const byte of data) {
    binary += String.fromCharCode(byte)
  }

  return btoa(binary)
}

/**
 * Der Fingerabdruck in Zweiergruppen, wie ihn ein Mensch vergleicht.
 * `a1b2c3…` liest niemand; `a1:b2:c3:…` schon.
 */
export function readable(fingerprint: string): string {
  return (fingerprint.match(/../g) ?? []).join(':')
}
