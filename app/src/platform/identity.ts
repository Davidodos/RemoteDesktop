/**
 * Wie dieses Gerät heißt — einmal gewählt, danach überall benutzt.
 *
 * <p>
 * **Der Befund dahinter:** der Name wurde bei *jeder* Kopplung neu eingetippt.
 * Wer drei Geräte koppelte, vergab denselben Namen dreimal — und wer nicht
 * selbst koppelte, sondern nur seinen Code vorzeigte, hieß drüben
 * <code>DESKTOP-4F2K9L1</code>, weil dann der Systemname einsprang.
 * </p>
 *
 * <p>
 * Der Name liegt deshalb **nativ** und nicht im Speicher der Weboberfläche: er
 * steht in <code>/api/info</code>, und das beantwortet auch ein Gerät, auf dem
 * gerade keine Seite offen ist. Am Rechner ist das die Datei
 * <code>{app}\data\devicename.txt</code> (<code>setup/DeviceNameFile.cs</code>),
 * am Handy <code>HostPreference</code>.
 * </p>
 */
export interface DeviceIdentity {
  /**
   * Der Stand: der Name, ob ihn jemand gewählt hat, und ob der Erststart
   * durch ist.
   */
  read(): Promise<IdentityState>

  rename(name: string): Promise<void>

  /** Der Erststart ist beantwortet — er wird nicht wieder gestellt. */
  finishFirstRun(): Promise<void>
}

export interface IdentityState {
  /** Der gewählte Name, sonst der des Systems. Nie leer. */
  name: string
  /**
   * Ob der Name gewählt wurde. `false` heißt: was in `name` steht, ist ein
   * Vorschlag — der Windows-Name oder das Handy-Modell.
   */
  chosen: boolean
  /**
   * Ob der Erststart schon lief. Am Rechner immer `true`: dort führt der
   * Einrichtungsassistent, und die Seite fragt nichts nach, was er schon
   * gefragt hat.
   */
  firstRunDone: boolean
}

/**
 * Für Umgebungen ohne eigene Kennung — den Browser. Dort gibt es kein Gerät,
 * das ein anderes ansprechen könnte, also auch keinen Namen, den jemand
 * bräuchte.
 */
export const noIdentity: DeviceIdentity = {
  read: (): Promise<IdentityState> =>
    Promise.resolve({ name: 'Browser', chosen: false, firstRunDone: true }),
  rename: (): Promise<void> => Promise.resolve(),
  finishFirstRun: (): Promise<void> => Promise.resolve(),
}
