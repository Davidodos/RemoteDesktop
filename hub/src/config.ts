import { readFile } from 'node:fs/promises'
import { z } from 'zod'

/**
 * Ein steuerbarer Rechner.
 *
 * `host` ist immer ein MagicDNS-Name — keine LAN-IPs, damit im Netz und
 * unterwegs derselbe Code läuft.
 */
const deviceSchema = z.object({
  id: z
    .string()
    .min(1)
    .regex(/^[a-z0-9-]+$/, 'Nur Kleinbuchstaben, Ziffern und Bindestrich.'),
  name: z.string().min(1),
  host: z
    .string()
    .min(1)
    .refine((value) => !/^\d+\.\d+\.\d+\.\d+$/.test(value), {
      message: 'Keine IP-Adressen — MagicDNS-Namen verwenden (siehe CLAUDE.md).',
    }),
  port: z.number().int().min(1).max(65535).default(8443),
  /** Für Wake-on-LAN. Ohne MAC ist das Gerät nur weckbar, wenn es schon läuft. */
  mac: z
    .string()
    .regex(/^([0-9a-fA-F]{2}[:-]){5}[0-9a-fA-F]{2}$/, 'MAC-Format erwartet: AA:BB:CC:DD:EE:FF')
    .optional(),
  /** Pre-Shared-Token des Agents auf diesem Rechner. */
  token: z.string().min(32, 'Agent-Token muss mindestens 32 Zeichen haben.'),
})

const configSchema = z.object({
  /**
   * Schützt den Hub selbst. Nötig, weil der Hub die Agent-Tokens an die App
   * ausliefert — ohne das könnte jedes Gerät im Tailnet sie abholen.
   */
  hubToken: z.string().min(32, 'Hub-Token muss mindestens 32 Zeichen haben.'),
  /** Broadcast-Adresse für Magic Packets. */
  broadcastAddress: z.string().default('255.255.255.255'),
  devices: z.array(deviceSchema).min(1),
})

export type Device = z.infer<typeof deviceSchema>
export type HubConfig = z.infer<typeof configSchema>

/**
 * Was die App über ein Gerät erfahren darf. Das Agent-Token ist bewusst
 * enthalten — die App verbindet direkt zum Agent, damit der Video-Stream
 * nicht über die NAS läuft.
 */
export type DeviceView = Omit<Device, 'mac'> & { canWake: boolean }

export function toDeviceView(device: Device): DeviceView {
  const { mac, ...rest } = device
  return { ...rest, canWake: mac !== undefined }
}

/**
 * Lädt und validiert die Gerätekonfiguration.
 *
 * Fehlkonfiguration soll beim Start knallen, nicht erst beim ersten
 * Tastendruck auf dem Handy.
 */
export async function loadConfig(path: string): Promise<HubConfig> {
  let raw: string

  try {
    raw = await readFile(path, 'utf8')
  } catch (cause) {
    throw new Error(
      `Gerätekonfiguration ${path} nicht lesbar. ` +
        'Vorlage: devices.example.json kopieren und ausfüllen.',
      { cause },
    )
  }

  let parsed: unknown

  try {
    parsed = JSON.parse(raw)
  } catch (cause) {
    throw new Error(`${path} ist kein gültiges JSON.`, { cause })
  }

  const result = configSchema.safeParse(parsed)

  if (!result.success) {
    const details = result.error.issues
      .map((issue) => `  ${issue.path.join('.') || '(Wurzel)'}: ${issue.message}`)
      .join('\n')

    throw new Error(`${path} ist ungültig:\n${details}`)
  }

  const ids = result.data.devices.map((device) => device.id)
  const duplicates = ids.filter((id, index) => ids.indexOf(id) !== index)

  if (duplicates.length > 0) {
    throw new Error(`Doppelte Geräte-IDs in ${path}: ${[...new Set(duplicates)].join(', ')}`)
  }

  return result.data
}
