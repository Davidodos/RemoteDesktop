using RemoteDesktopSetup;

namespace RemoteDesktopAgent.Auth;

/// <summary>
/// Das Geheimnis für die nur lokal erreichbaren Endpunkte — siehe
/// <see cref="LocalSecretFile"/>.
///
/// Gelesen wird bei jedem Aufruf und nicht einmal beim Start: die Datei kann
/// nach dem Start entstehen, wenn das Fenster zuerst kommt. Die Aufrufe sind
/// selten (ein Kopplungscode, ein Eintrag), das Lesen kostet nichts.
/// </summary>
public sealed class LocalSecret(string path)
{
    /// <summary>Legt das Geheimnis an, falls es fehlt. Für das Log, wenn es scheitert.</summary>
    public string? Ensure()
    {
        try
        {
            LocalSecretFile.LoadOrCreate(path);

            return null;
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            return failure.Message;
        }
    }

    /// <summary>Ob das vorgelegte Geheimnis das aus der Datei ist.</summary>
    public bool Matches(string? presented) =>
        LocalSecretFile.Matches(presented, LocalSecretFile.Read(path));
}
