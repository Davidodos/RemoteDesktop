import { storage } from './storage.ts'

/** Eine gespeicherte Tastenkombination mit eigenem Namen. */
export interface Shortcut {
  id: string
  label: string
  /** Tastennamen des Protokolls, in der Reihenfolge des Drückens. */
  keys: string[]
}

/**
 * Was ohne Zutun bereitsteht.
 *
 * Win+Tab statt Alt+Tab, weil die Fensterübersicht stehen bleibt — Alt+Tab
 * bräuchte ein gehaltenes Alt, um sich durchzuschalten, und das ist auf einem
 * Handy keine Geste.
 */
export const DEFAULT_SHORTCUTS: Shortcut[] = [
  { id: 'window-overview', label: 'Fenster-Übersicht', keys: ['win', 'tab'] },
  { id: 'task-manager', label: 'Task-Manager', keys: ['ctrl', 'shift', 'escape'] },
  { id: 'copy', label: 'Kopieren', keys: ['ctrl', 'c'] },
  { id: 'paste', label: 'Einfügen', keys: ['ctrl', 'v'] },
]

/** Neuer Bezeichner für einen selbst angelegten Shortcut. */
export function makeShortcutId(): string {
  return `s${Date.now().toString(36)}${Math.random().toString(36).slice(2, 6)}`
}

/**
 * Liest die gespeicherten Shortcuts.
 *
 * Alles wird geprüft, statt dem localStorage zu glauben: dort steht, was eine
 * frühere Fassung der App hinterlassen hat, und ein kaputter Eintrag darf nicht
 * die ganze Liste kosten.
 */
export function parseShortcuts(raw: string | undefined): Shortcut[] {
  if (raw === undefined) {
    return DEFAULT_SHORTCUTS
  }

  try {
    const parsed: unknown = JSON.parse(raw)

    return Array.isArray(parsed) ? parsed.flatMap(toShortcut) : DEFAULT_SHORTCUTS
  } catch {
    return DEFAULT_SHORTCUTS
  }
}

function toShortcut(entry: unknown): Shortcut[] {
  if (typeof entry !== 'object' || entry === null) {
    return []
  }

  const { id, label, keys } = entry as Record<string, unknown>

  if (typeof id !== 'string' || typeof label !== 'string' || !Array.isArray(keys)) {
    return []
  }

  const valid = keys.filter((key): key is string => typeof key === 'string' && key.length > 0)

  // Ein Shortcut ohne Tasten wäre ein Knopf, der nichts tut.
  return valid.length === 0 || label.length === 0 ? [] : [{ id, label, keys: valid }]
}

export function loadShortcuts(): Shortcut[] {
  return parseShortcuts(storage.getShortcuts())
}

export function saveShortcuts(shortcuts: Shortcut[]): void {
  storage.setShortcuts(JSON.stringify(shortcuts))
}

/** Legt einen Shortcut an oder ersetzt den gleichnamigen. */
export function upsertShortcut(shortcuts: readonly Shortcut[], entry: Shortcut): Shortcut[] {
  return shortcuts.some((existing) => existing.id === entry.id)
    ? shortcuts.map((existing) => (existing.id === entry.id ? entry : existing))
    : [...shortcuts, entry]
}

export function removeShortcut(shortcuts: readonly Shortcut[], id: string): Shortcut[] {
  return shortcuts.filter((entry) => entry.id !== id)
}
