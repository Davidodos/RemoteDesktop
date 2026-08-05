#!/usr/bin/env node
/**
 * Erzeugt das Schlüsselpaar, mit dem Release-Manifeste unterschrieben werden.
 *
 * Einmal aufrufen, dann:
 *   - den öffentlichen Teil in `agent/Services/ReleaseManifest.cs` bei
 *     `ReleaseKeys.PublicKey` eintragen und einchecken,
 *   - den privaten Teil als Repository-Secret `RELEASE_PRIVATE_KEY` hinterlegen
 *     und die Datei danach löschen.
 *
 * **Der private Schlüssel darf niemals ins Repo.** Wer ihn hat, kann jedem
 * Agent eine beliebige .exe unterschieben — und der Agent hat vollständige
 * Kontrolle über den Rechner. Genau dagegen ist die Signatur da; ein Hash aus
 * derselben Quelle wie die Datei schützt nur gegen abgebrochene Downloads.
 */
import { generateKeyPairSync } from 'node:crypto'

const { publicKey, privateKey } = generateKeyPairSync('ec', { namedCurve: 'prime256v1' })

const spki = publicKey.export({ format: 'der', type: 'spki' }).toString('base64')
const pkcs8 = privateKey.export({ format: 'pem', type: 'pkcs8' })

console.log('--- Öffentlicher Schlüssel (SPKI, Base64) ---')
console.log('In agent/Services/ReleaseManifest.cs eintragen:\n')
console.log(`    public const string PublicKey = "${spki}";\n`)

console.log('--- Privater Schlüssel (PKCS#8, PEM) ---')
console.log('Als Repository-Secret RELEASE_PRIVATE_KEY hinterlegen, dann vergessen:\n')
console.log(pkcs8)
