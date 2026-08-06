#!/usr/bin/env node
/**
 * Erzeugt aus `assets/icon.svg` alle Symbole, die das Projekt braucht.
 *
 * Aufruf (sharp ist keine Abhängigkeit des Projekts — es wird nur hier
 * gebraucht, und Symbole ändern sich fast nie):
 *
 *   npm install --no-save sharp
 *   node scripts/icons.mjs
 *
 * Warum ein Skript und nicht acht abgelegte Dateien: es gibt **ein** Zeichen,
 * und es soll auf dem Handy dasselbe sein wie auf dem Rechner. Zwölf einzeln
 * gepflegte PNG-Dateien driften auseinander, sobald jemand eine davon anfasst.
 * Die erzeugten Dateien liegen trotzdem im Repo — Android und der C#-Build
 * müssen sie beim Bauen vorfinden, und niemand soll dafür einen Rasterizer
 * installieren müssen.
 *
 * Zwei Quellen, nicht eine: `icon-small.svg` ist dasselbe Motiv für 16 bis 32
 * Pixel. Die Begründung steht in der Datei.
 */
import { mkdirSync, readFileSync, writeFileSync } from 'node:fs'
import { dirname, join } from 'node:path'
import { fileURLToPath } from 'node:url'

const sharp = await import('sharp')
  .then((module) => module.default)
  .catch(() => {
    console.error('sharp fehlt. Einmal `npm install --no-save sharp` und noch einmal.')
    process.exit(1)
  })

const root = join(dirname(fileURLToPath(import.meta.url)), '..')

const large = readFileSync(join(root, 'assets', 'icon.svg'))
const small = readFileSync(join(root, 'assets', 'icon-small.svg'))

/** Ab hier lohnt sich das große Motiv mit Zeiger; darunter das gröbere. */
const SmallUpTo = 32

/** Der Anteil der Fläche, den Android einem adaptiven Symbol wirklich zeigt. */
const AdaptiveSafeArea = 72 / 108

/** Die Bildschirmklassen von Android, als Vielfache von mdpi. */
const Densities = [
  ['mdpi', 1],
  ['hdpi', 1.5],
  ['xhdpi', 2],
  ['xxhdpi', 3],
  ['xxxhdpi', 4]
]

/**
 * Eine Größe als PNG. Die hohe `density` ist nötig, weil sharp das SVG sonst
 * in seiner nominalen Größe rastert und danach herunterrechnet — bei 512
 * Pixeln Ausgabe sieht man das nicht, bei 16 sehr wohl.
 */
const render = (size) =>
  sharp(size <= SmallUpTo ? small : large, { density: 900 })
    .resize(size, size)
    .png({ compressionLevel: 9 })
    .toBuffer()

const write = async (path, buffer) => {
  mkdirSync(dirname(path), { recursive: true })
  writeFileSync(path, buffer)
  console.log(path.slice(root.length + 1))
}

// ---- Windows -----------------------------------------------------------
// Eine .ico ist ein Verzeichnis aus Einzelbildern. Windows sucht sich je nach
// Ort die passende Größe: 16 in der Titelzeile, 32 in der Taskleiste, 256 im
// Explorer.
//
// Bis 64 Pixel als unkomprimiertes DIB, darüber als PNG. Das ist keine
// Vorliebe, sondern der Weg mit der breitesten Unterstützung: PNG in einer
// .ico versteht Windows erst ab Vista, und einige ältere Ladepfade in
// System.Drawing greifen bei kleinen Größen weiterhin zum DIB.
const IcoSizes = [16, 20, 24, 32, 40, 48, 64, 128, 256]

/**
 * Ein Einzelbild im alten Format: Kopf, Farbwerte von unten nach oben, danach
 * eine Maske aus Nullen. Die Maske ist bei 32 Bit bedeutungslos — fehlt sie,
 * zeichnet Windows das Symbol trotzdem, aber manche Ladepfade rechnen die
 * Größe aus dem Kopf und schneiden dann die unterste Zeile ab.
 */
const dib = (rgba, size) => {
  const header = Buffer.alloc(40)

  header.writeUInt32LE(40, 0)
  header.writeInt32LE(size, 4)
  header.writeInt32LE(size * 2, 8) // Bild und Maske zusammen — so will es das Format.
  header.writeUInt16LE(1, 12)
  header.writeUInt16LE(32, 14)
  header.writeUInt32LE(size * size * 4, 20)

  const pixels = Buffer.alloc(size * size * 4)

  for (let y = 0; y < size; y++) {
    for (let x = 0; x < size; x++) {
      const from = (y * size + x) * 4
      const to = ((size - 1 - y) * size + x) * 4

      pixels[to] = rgba[from + 2]
      pixels[to + 1] = rgba[from + 1]
      pixels[to + 2] = rgba[from]
      pixels[to + 3] = rgba[from + 3]
    }
  }

  return Buffer.concat([header, pixels, Buffer.alloc((size / 8) * size)])
}

const buildIco = async () => {
  const entries = []

  for (const size of IcoSizes) {
    const source = size <= SmallUpTo ? small : large

    if (size > 64) {
      entries.push({ size, data: await render(size) })
      continue
    }

    const { data } = await sharp(source, { density: 900 })
      .resize(size, size)
      .raw()
      .toBuffer({ resolveWithObject: true })

    entries.push({ size, data: dib(data, size) })
  }

  const directory = Buffer.alloc(6 + entries.length * 16)

  directory.writeUInt16LE(0, 0)
  directory.writeUInt16LE(1, 2)
  directory.writeUInt16LE(entries.length, 4)

  let offset = directory.length

  entries.forEach((entry, index) => {
    const at = 6 + index * 16

    // 256 wird als 0 geschrieben — ein Byte kann die Zahl nicht fassen.
    directory.writeUInt8(entry.size === 256 ? 0 : entry.size, at)
    directory.writeUInt8(entry.size === 256 ? 0 : entry.size, at + 1)
    directory.writeUInt16LE(1, at + 4)
    directory.writeUInt16LE(32, at + 6)
    directory.writeUInt32LE(entry.data.length, at + 8)
    directory.writeUInt32LE(offset, at + 12)

    offset += entry.data.length
  })

  return Buffer.concat([directory, ...entries.map((entry) => entry.data)])
}

// ---- Android -----------------------------------------------------------
// Drei Sätze: das alte quadratische Symbol, das runde für Geräte, die es
// wollen, und die Vordergrundebene für adaptive Symbole ab Android 8. Die
// Hintergrundebene ist eine Farbe (res/values/ic_launcher_background.xml) und
// entsteht deshalb nicht hier.

const rounded = async (size) => {
  const circle = Buffer.from(
    `<svg xmlns="http://www.w3.org/2000/svg" width="${size}" height="${size}">` +
      `<circle cx="${size / 2}" cy="${size / 2}" r="${size / 2}" fill="#fff"/></svg>`
  )

  return sharp(await render(size))
    .composite([{ input: circle, blend: 'dest-in' }])
    .png({ compressionLevel: 9 })
    .toBuffer()
}

/**
 * Die Vordergrundebene. Das Motiv wird in die Fläche gesetzt, die Android
 * garantiert zeigt — der Rand darum bleibt durchsichtig, weil das System ihn
 * je nach Hersteller beschneidet, dreht oder wackeln lässt.
 *
 * Die Kachel bleibt dabei stehen, nur ihre Ecken werden weggeschnitten. Sie
 * hat genau die Farbe der Hintergrundebene (res/values/ic_launcher_background.xml),
 * und deshalb ist die Naht zwischen beiden Ebenen unsichtbar — auch dann, wenn
 * ein Launcher die Ebenen gegeneinander verschiebt.
 */
const foreground = async (size) => {
  const inner = Math.round(size * AdaptiveSafeArea)
  const motif = await sharp(large, { density: 900 }).resize(inner, inner).toBuffer()

  // Die Kachelfarbe wegschneiden: übrig bleibt, was auf ihr liegt. Ein
  // Rechteck in der Hintergrundfarbe zu belassen wäre bei runden Masken
  // sichtbar.
  const cut = Buffer.from(
    `<svg xmlns="http://www.w3.org/2000/svg" width="${inner}" height="${inner}">` +
      `<rect width="${inner}" height="${inner}" rx="${Math.round(inner * 0.219)}" fill="#fff"/></svg>`
  )

  const trimmed = await sharp(motif)
    .composite([{ input: cut, blend: 'dest-in' }])
    .toBuffer()

  return sharp({
    create: {
      width: size,
      height: size,
      channels: 4,
      background: { r: 0, g: 0, b: 0, alpha: 0 }
    }
  })
    .composite([{ input: trimmed, left: (size - inner) >> 1, top: (size - inner) >> 1 }])
    .png({ compressionLevel: 9 })
    .toBuffer()
}

const android = join(root, 'clients', 'android', 'android', 'app', 'src', 'main', 'res')

// ---- Alles schreiben ---------------------------------------------------

await write(join(root, 'desktop', 'RemoteDesktop.ico'), await buildIco())

for (const [density, factor] of Densities) {
  const target = join(android, `mipmap-${density}`)

  await write(join(target, 'ic_launcher.png'), await render(48 * factor))
  await write(join(target, 'ic_launcher_round.png'), await rounded(48 * factor))
  await write(join(target, 'ic_launcher_foreground.png'), await foreground(108 * factor))
}

for (const size of [192, 512]) {
  await write(join(root, 'app', 'public', `icon-${size}.png`), await render(size))
}
