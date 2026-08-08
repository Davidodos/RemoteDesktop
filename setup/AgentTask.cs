using System.Security;

namespace RemoteDesktopSetup;

/// <summary>
/// Der Agent als geplante Aufgabe — und warum er kein Windows-Dienst mehr ist.
///
/// <para>
/// **Der Befund dahinter:** ein Dienst läuft unter <c>SYSTEM</c> in Sitzung 0.
/// Dort gibt es keinen Bildschirm und keinen Desktop, auf den sich etwas
/// schreiben ließe. Am echten Gerät sah das so aus: die Bildaufnahme meldete
/// „Kein Grafikausgang für Monitor 'WinDisc'“ — das ist der Platzhaltermonitor,
/// den Sitzung 0 vorzeigt —, und jede Eingabe scheiterte an
/// <c>SendInput</c>-Fehler 5, also an der Trennung zwischen Sitzung 0 und dem
/// Desktop des angemeldeten Menschen. Kein Schalter behebt das; es ist die
/// Bauweise.
/// </para>
///
/// <para>
/// Also läuft der Agent dort, wo der Bildschirm ist: in der Sitzung des
/// angemeldeten Benutzers, mit den höchsten Rechten, die dieser Benutzer hat.
/// Ausgelöst wird er bei der Anmeldung. Der Preis ist ausgesprochen: **ohne
/// angemeldeten Benutzer läuft er nicht.** Ein aufgeweckter Rechner ist also
/// erst nach der Anmeldung erreichbar.
/// </para>
///
/// <para>
/// Angelegt wird die Aufgabe aus einer XML-Beschreibung statt über die Schalter
/// von <c>schtasks</c>: nur so lassen sich „läuft ohne Zeitlimit“, „nicht auf
/// Akku anhalten“ und vor allem „**kein** Auslöser“ überhaupt ausdrücken — und
/// die Schalter verhalten sich je nach Sprache des Systems unterschiedlich.
/// </para>
/// </summary>
public static class AgentTask
{
    /// <summary>So heißt die Aufgabe — und vorher hieß so der Dienst.</summary>
    public const string Name = Autostart.ServiceName;

    /// <summary>
    /// Die XML-Beschreibung der Aufgabe.
    /// </summary>
    /// <param name="executable">Volle Pfadangabe zu <c>RemoteDesktopAgent.exe</c>.</param>
    /// <param name="user">
    /// Der Benutzer, in dessen Sitzung der Agent läuft — <c>DOMÄNE\Name</c>.
    /// Er wird ausdrücklich übergeben und nicht ermittelt: die Aufgabe wird aus
    /// einem erhöhten Aufruf angelegt, und der läuft bei einem Standardbenutzer
    /// unter einem *anderen* Konto als dem, das gerade am Rechner sitzt.
    /// </param>
    /// <param name="atLogon">
    /// Ob sie bei der Anmeldung von allein losläuft. Ohne Auslöser bleibt die
    /// Aufgabe bestehen und wird nur von Hand gestartet — „nicht automatisch
    /// starten“ heißt nicht „weg damit“.
    /// </param>
    public static string Definition(string executable, string user, bool atLogon)
    {
        var exe = SecurityElement.Escape(executable) ?? string.Empty;
        var who = SecurityElement.Escape(user) ?? string.Empty;

        var trigger = atLogon
            ? $"""
                   <LogonTrigger>
                     <Enabled>true</Enabled>
                     <UserId>{who}</UserId>
                   </LogonTrigger>
               """
            : string.Empty;

        // ExecutionTimeLimit PT0S: kein Zeitlimit. Ohne das beendet Windows die
        // Aufgabe nach drei Tagen — ein Rechner, der seit einer Woche läuft,
        // wäre unerreichbar, und niemand käme auf den Grund.
        return $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo>
                <Description>Macht diesen Rechner über RemoteDesktop fernsteuerbar.</Description>
                <URI>\{Name}</URI>
              </RegistrationInfo>
              <Triggers>
            {trigger}
              </Triggers>
              <Principals>
                <Principal id="Author">
                  <UserId>{who}</UserId>
                  <LogonType>InteractiveToken</LogonType>
                  <RunLevel>HighestAvailable</RunLevel>
                </Principal>
              </Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <AllowHardTerminate>true</AllowHardTerminate>
                <StartWhenAvailable>false</StartWhenAvailable>
                <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
                <IdleSettings>
                  <StopOnIdleEnd>false</StopOnIdleEnd>
                  <RestartOnIdle>false</RestartOnIdle>
                </IdleSettings>
                <AllowStartOnDemand>true</AllowStartOnDemand>
                <Enabled>true</Enabled>
                <Hidden>false</Hidden>
                <RunOnlyIfIdle>false</RunOnlyIfIdle>
                <WakeToRun>false</WakeToRun>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <Priority>5</Priority>
                <RestartOnFailure>
                  <Interval>PT1M</Interval>
                  <Count>3</Count>
                </RestartOnFailure>
              </Settings>
              <Actions Context="Author">
                <Exec>
                  <Command>{exe}</Command>
                </Exec>
              </Actions>
            </Task>
            """;
    }

    /// <summary>Ob diese Beschreibung bei der Anmeldung von allein startet.</summary>
    public static bool StartsAtLogon(string? definition) =>
        definition?.Contains("<LogonTrigger>", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>
    /// Startart und Benutzer in einer Zeichenkette — der eine Zusatz, den ein
    /// erhöhter Auftrag mitbekommt (siehe <c>desktop/Elevation.cs</c>).
    /// </summary>
    public static string Argument(bool atLogon, string user) =>
        $"{(atLogon ? "auto" : "demand")}|{user}";

    /// <summary>Der umgekehrte Weg. Fehlt der Benutzer, bleibt er leer.</summary>
    public static (bool AtLogon, string User) ReadArgument(string? argument)
    {
        var parts = (argument ?? string.Empty).Split('|', 2);

        return (parts[0] == "auto", parts.Length > 1 ? parts[1].Trim() : string.Empty);
    }
}
