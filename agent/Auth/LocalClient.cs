using RemoteDesktopSetup;

namespace RemoteDesktopAgent.Auth;

/// <summary>
/// Der Ausweis der Oberfläche dieses Rechners — der öffentliche Schlüssel, mit
/// dem sich das Fenster nebenan bei fremden Geräten anmeldet.
///
/// <para>
/// **Wozu der Agent ihn kennt:** eine Kopplung geht immer in beide Richtungen.
/// Wer sich hier koppelt, soll ohne einen zweiten Aufruf auch von hier aus
/// erreichbar werden — dafür braucht er den Schlüssel dieser Oberfläche, und
/// bekommt ihn in der Antwort auf <c>/api/pair</c>.
/// </para>
///
/// <para>
/// **Woher der Agent ihn hat:** aus <c>clientkey.json</c> im Datenordner, und
/// damit aus derselben Datei, aus der das Fenster ihn liest. Bis zum 16.08.2026
/// hinterlegte ihn die React-App über <c>/api/pair/local</c> — und wer das
/// Fenster öffnete, ohne die Fernsteuerung anzuzeigen, hinterlegte gar nichts.
/// Die Gegenseite bekam dann ein leeres <c>clientKey</c>, ohne dass irgendwo
/// stand, warum. Siehe <see cref="ClientKeyFile"/>.
/// </para>
///
/// <para>
/// Gelesen wird bei jedem Zugriff und nicht einmal beim Start: die Datei kann
/// nach dem Start des Agents entstehen — nämlich dann, wenn das Fenster zuerst
/// kommt. Ein zwischengespeichertes <c>null</c> hielte diesen Agent bis zum
/// nächsten Neustart für ausweislos.
/// </para>
///
/// <para>
/// Es ist ein öffentlicher Schlüssel. Er verrät nichts und erlaubt nichts —
/// Macht bekommt er erst dadurch, dass ihn die Gegenseite in ihre eigene
/// <c>clients.json</c> aufnimmt, und das tut sie nur nach einer bestandenen
/// Kopplung.
/// </para>
/// </summary>
public sealed class LocalClient(string path)
{
    /// <summary><c>null</c>, solange die Datei noch nicht da ist.</summary>
    public string? PublicKey => ClientKeyFile.Read(path)?.PublicKey;

    /// <summary>
    /// Legt das Schlüsselpaar an, falls es noch keins gibt.
    ///
    /// <para>
    /// Der Agent läuft mit den höchsten verfügbaren Rechten und kommt in den
    /// Datenordner hinein; das Fenster nur, wenn der Installer den Ordner dafür
    /// freigegeben hat. Wer zuerst kommt, legt die Datei an — deshalb tun es
    /// beide, und keiner von beiden überschreibt sie.
    /// </para>
    /// </summary>
    /// <returns>
    /// Ein Satz, wenn es nicht geklappt hat — für das Log. Der Agent startet
    /// trotzdem: ohne Ausweis bleibt eine Kopplung einseitig, aber steuerbar
    /// ist dieser Rechner deswegen nicht weniger.
    /// </returns>
    public string? Ensure()
    {
        try
        {
            ClientKeyFile.LoadOrCreate(path);

            return null;
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            return failure.Message;
        }
    }
}
