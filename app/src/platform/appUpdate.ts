import type { UpdateInfo } from './index.ts'

/**
 * Wo die Releases der App liegen. Dasselbe Repository wie beim Agent — eine
 * Ausgabe enthält beide Dateien, damit Rechner und Handy zusammenpassen.
 */
export const RELEASE_REPOSITORY = 'Davidodos/RemoteDesktop'

/** Der Anhang, der die App enthält. */
export const APK_ASSET = 'remotedesktop.apk'

/**
 * Sucht im jüngsten Release die APK und ihre Fassung.
 *
 * Bewusst ohne eigene Signaturprüfung, anders als beim Agent: Android lässt
 * eine APK nur über eine bereits installierte drüber, wenn sie mit **demselben
 * Schlüssel** unterschrieben ist. Eine untergeschobene Datei scheitert daran,
 * bevor irgendetwas von ihr läuft — das ist eine stärkere Zusage, als eine
 * selbstgebaute Prüfung sie geben könnte.
 *
 * @param fetchJson Hereingereicht, damit sich das ohne Netz prüfen lässt.
 */
export async function findLatestApk(
  fetchJson: (url: string) => Promise<unknown>,
  repository = RELEASE_REPOSITORY,
): Promise<UpdateInfo | undefined> {
  let release: unknown

  try {
    release = await fetchJson(`https://api.github.com/repos/${repository}/releases/latest`)
  } catch {
    // Kein Netz, kein Release, ein Fehler von GitHub — für „gibt es etwas
    // Neues?" ist das alles dasselbe.
    return undefined
  }

  if (typeof release !== 'object' || release === null) {
    return undefined
  }

  const { tag_name: tag, assets } = release as { tag_name?: unknown; assets?: unknown }

  if (typeof tag !== 'string' || !Array.isArray(assets)) {
    return undefined
  }

  const url = assets
    .filter((asset): asset is { name?: unknown; browser_download_url?: unknown } =>
      typeof asset === 'object' && asset !== null,
    )
    .find((asset) => asset.name === APK_ASSET)?.browser_download_url

  if (typeof url !== 'string' || url.length === 0) {
    return undefined
  }

  return { version: stripTagPrefix(tag), url }
}

/** `v1.2.0` und `1.2.0` sollen dasselbe bedeuten. */
export function stripTagPrefix(tag: string): string {
  return tag.startsWith('v') ? tag.slice(1) : tag
}

/**
 * Ob die angebotene Fassung eine andere ist als die laufende.
 *
 * Verglichen wird auf Ungleichheit und nicht auf „größer": eine
 * zurückgenommene Ausgabe soll ebenfalls angeboten werden. Was tatsächlich
 * installiert werden darf, entscheidet ohnehin Android — eine ältere
 * `versionCode` lehnt es von sich aus ab.
 */
export function isDifferentVersion(offered: string, installed: string | undefined): boolean {
  if (installed === undefined || installed.length === 0) {
    return true
  }

  return stripTagPrefix(offered) !== stripTagPrefix(installed)
}
