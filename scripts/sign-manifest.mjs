#!/usr/bin/env node
/**
 * Baut `manifest.json` zur fertigen Agent-Datei und unterschreibt es.
 *
 * Aufruf:
 *   node scripts/sign-manifest.mjs <datei> <version> <protokoll> <ausgabeordner> [name]
 *
 * `name` ist der Dateiname des Manifests; ohne Angabe `manifest.json`. Zwei
 * Manifeste in einem Release, weil es zwei Dinge zu tauschen gibt: die
 * Programmdatei des Agents (er tauscht sie im Lauf gegen sich selbst) und den
 * Installer (er erneuert zusätzlich das Fenster und die Oberfläche). Beide
 * werden auf demselben Rechner mit vollen Rechten ausgeführt, also gilt für
 * beide dieselbe Bedingung: nicht ohne gültige Unterschrift.
 *
 * Der private Schlüssel kommt aus der Umgebung (`RELEASE_PRIVATE_KEY`, PEM) —
 * nie von der Kommandozeile, weil er dort in der Prozessliste stünde.
 *
 * Unterschrieben werden die **Bytes der Manifestdatei**, nicht ein daraus
 * gebautes Objekt. Sonst hinge die Prüfung daran, dass beide Seiten dieselbe
 * JSON-Schreibweise wählen, und das ist keine Grundlage für eine Signatur.
 */
import { createHash, createPrivateKey, sign } from 'node:crypto'
import { readFileSync, statSync, writeFileSync } from 'node:fs'
import { basename, join } from 'node:path'

const [file, version, protocol, outputDirectory, name = 'manifest.json'] = process.argv.slice(2)

if (file === undefined || version === undefined || protocol === undefined || outputDirectory === undefined) {
  console.error('Aufruf: sign-manifest.mjs <datei> <version> <protokoll> <ausgabeordner>')
  process.exit(1)
}

const pem = process.env.RELEASE_PRIVATE_KEY

if (pem === undefined || pem.length === 0) {
  console.error('RELEASE_PRIVATE_KEY fehlt — ohne ihn gibt es kein gültiges Release.')
  process.exit(1)
}

const content = readFileSync(file)

const manifest = {
  version,
  protocol: Number(protocol),
  file: basename(file),
  size: statSync(file).size,
  sha256: createHash('sha256').update(content).digest('hex'),
}

// Ohne abschließenden Zeilenumbruch und in genau dieser Reihenfolge: die Bytes
// sind es, die unterschrieben werden.
const bytes = Buffer.from(JSON.stringify(manifest, null, 2), 'utf8')

// Dasselbe Format wie bei der Kopplung: r und s hintereinander (IEEE P1363),
// nicht DER. .NET prüft ausdrücklich in diesem Format.
const signature = sign('sha256', bytes, {
  key: createPrivateKey(pem),
  dsaEncoding: 'ieee-p1363',
}).toString('base64')

writeFileSync(join(outputDirectory, name), bytes)
writeFileSync(join(outputDirectory, `${name}.sig`), signature)

console.log(`${name} für ${manifest.file} (${manifest.size} Bytes) unterschrieben.`)
