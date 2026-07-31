/**
 * Hält die laufende Sitzung am Leben, während die App im Hintergrund ist.
 *
 * Nur Android hat hier etwas zu tun: dort drosselt das System eine WebView im
 * Hintergrund, der Eingabe-Socket fällt zu und der Videostrom pausiert. Ein
 * Vordergrunddienst nimmt dem System diese Entscheidung ab. Im Browser gibt es
 * kein Gegenstück — dort bleibt es beim Drosseln —, im Windows-Fenster ist es
 * nicht nötig.
 *
 * Beide Aufrufe dürfen mehrfach kommen und müssen das aushalten: die
 * Geräteauswahl wechselt öfter, als eine Sitzung endet.
 *
 * Steht wie `errors.ts` in einer eigenen Datei, damit die Umsetzungen den
 * Vorgabewert benutzen können, ohne dass zwischen `index.ts` und ihnen ein
 * Ringschluss entsteht.
 */
export interface SessionKeepAlive {
  begin(deviceName: string): Promise<void>
  end(): Promise<void>
}

/**
 * Für die Plattformen, deren Sitzung von allein weiterläuft. Als eigener Wert
 * statt als optionales Feld, damit die Aufrufstelle in `App.tsx` ohne
 * Fallunterscheidung auskommt.
 */
export const noSessionKeepAlive: SessionKeepAlive = {
  begin: () => Promise.resolve(),
  end: () => Promise.resolve(),
}
