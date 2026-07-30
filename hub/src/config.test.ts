import { mkdtemp, writeFile } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { describe, expect, it } from 'vitest'
import { loadConfig, toDeviceView } from './config.js'

const TOKEN = 'x'.repeat(40)

const validConfig = {
  hubToken: TOKEN,
  devices: [
    { id: 'pc', name: 'PC', host: 'pc.example.ts.net', port: 8443, mac: 'AA:BB:CC:DD:EE:FF', token: TOKEN },
    { id: 'laptop', name: 'Laptop', host: 'laptop.example.ts.net', port: 8443, token: TOKEN },
  ],
}

async function writeConfig(content: unknown): Promise<string> {
  const directory = await mkdtemp(join(tmpdir(), 'hub-config-'))
  const path = join(directory, 'devices.json')
  await writeFile(path, typeof content === 'string' ? content : JSON.stringify(content))
  return path
}

describe('loadConfig', () => {
  it('lädt eine gültige Konfiguration', async () => {
    // Arrange
    const path = await writeConfig(validConfig)

    // Act
    const config = await loadConfig(path)

    // Assert
    expect(config.devices).toHaveLength(2)
    expect(config.devices[0]?.name).toBe('PC')
  })

  it('setzt die Broadcast-Adresse auf den Standard, wenn sie fehlt', async () => {
    const config = await loadConfig(await writeConfig(validConfig))

    expect(config.broadcastAddress).toBe('255.255.255.255')
  })

  it('setzt Port 8443, wenn er fehlt', async () => {
    // Arrange
    const path = await writeConfig({
      hubToken: TOKEN,
      devices: [{ id: 'pc', name: 'PC', host: 'pc.example.ts.net', token: TOKEN }],
    })

    // Act
    const config = await loadConfig(path)

    // Assert
    expect(config.devices[0]?.port).toBe(8443)
  })

  it('meldet eine fehlende Datei mit Hinweis auf die Vorlage', async () => {
    await expect(loadConfig('/gibt/es/nicht.json')).rejects.toThrow(/devices.example.json/)
  })

  it('meldet kaputtes JSON', async () => {
    await expect(loadConfig(await writeConfig('{ kein json'))).rejects.toThrow(/gültiges JSON/)
  })

  it('lehnt LAN-IPs als Host ab', async () => {
    // Das Projekt läuft ausschließlich über Tailscale — eine IP im Host-Feld
    // wäre genau die Fallunterscheidung, die vermieden werden soll.
    const path = await writeConfig({
      hubToken: TOKEN,
      devices: [{ id: 'pc', name: 'PC', host: '192.168.178.33', token: TOKEN }],
    })

    await expect(loadConfig(path)).rejects.toThrow(/MagicDNS/)
  })

  it('lehnt zu kurze Tokens ab', async () => {
    const path = await writeConfig({
      hubToken: 'zukurz',
      devices: [{ id: 'pc', name: 'PC', host: 'pc.example.ts.net', token: TOKEN }],
    })

    await expect(loadConfig(path)).rejects.toThrow(/mindestens 32/)
  })

  it('lehnt eine ungültige MAC-Adresse ab', async () => {
    const path = await writeConfig({
      hubToken: TOKEN,
      devices: [{ id: 'pc', name: 'PC', host: 'pc.example.ts.net', mac: 'keine-mac', token: TOKEN }],
    })

    await expect(loadConfig(path)).rejects.toThrow(/MAC-Format/)
  })

  it('lehnt doppelte Geräte-IDs ab', async () => {
    // Sonst wäre nicht vorhersehbar, welcher Rechner heruntergefahren wird.
    const path = await writeConfig({
      hubToken: TOKEN,
      devices: [
        { id: 'pc', name: 'PC', host: 'pc.example.ts.net', token: TOKEN },
        { id: 'pc', name: 'PC zwei', host: 'pc2.example.ts.net', token: TOKEN },
      ],
    })

    await expect(loadConfig(path)).rejects.toThrow(/Doppelte Geräte-IDs/)
  })

  it('lehnt eine leere Geräteliste ab', async () => {
    await expect(loadConfig(await writeConfig({ hubToken: TOKEN, devices: [] }))).rejects.toThrow()
  })
})

describe('toDeviceView', () => {
  it('entfernt die MAC-Adresse und meldet stattdessen die Weckbarkeit', () => {
    // Arrange
    const device = {
      id: 'pc',
      name: 'PC',
      host: 'pc.example.ts.net',
      port: 8443,
      mac: 'AA:BB:CC:DD:EE:FF',
      token: TOKEN,
    }

    // Act
    const view = toDeviceView(device)

    // Assert — die MAC hat im Browser nichts verloren, das Aufwecken macht der Hub.
    expect(view).not.toHaveProperty('mac')
    expect(view.canWake).toBe(true)
  })

  it('meldet canWake false ohne MAC-Adresse', () => {
    const view = toDeviceView({
      id: 'laptop',
      name: 'Laptop',
      host: 'laptop.example.ts.net',
      port: 8443,
      token: TOKEN,
    })

    expect(view.canWake).toBe(false)
  })
})
