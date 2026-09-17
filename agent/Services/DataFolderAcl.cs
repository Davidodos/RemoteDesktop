using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace RemoteDesktopAgent.Services;

/// <summary>
/// Setzt die Rechte des Datenordners: <c>data</c> für jeden lesbar, der
/// Unterordner <c>secret</c> nur für Administratoren und das System.
///
/// <para>
/// Der Agent tut das selbst und verlässt sich nicht auf den Installer: Inno
/// Setup <em>ergänzt</em> Einträge und nimmt keine weg, und eine Installation
/// von vor v1.4 hatte <c>data</c> für jeden Benutzer beschreibbar. Bei jedem
/// Start ersetzt — die Rechte werden nicht vererbt, damit nichts aus
/// <c>Program Files</c> hereinsickert.
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
public static class DataFolderAcl
{
    /// <returns>Was nicht gesetzt werden konnte — für das Log.</returns>
    public static string? Apply(string dataDirectory, string secretDirectory)
    {
        try
        {
            Directory.CreateDirectory(secretDirectory);

            Set(dataDirectory, usersMayRead: true);
            Set(secretDirectory, usersMayRead: false);

            return null;
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException
                                             or System.Security.SecurityException)
        {
            return failure.Message;
        }
    }

    private static void Set(string directory, bool usersMayRead)
    {
        var info = new DirectoryInfo(directory);
        var security = new DirectorySecurity();

        // Nicht erben und alles Geerbte verwerfen. Danach gilt nur, was hier steht.
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

        var flags = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;

        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            FileSystemRights.FullControl, flags, PropagationFlags.None, AccessControlType.Allow));

        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            FileSystemRights.FullControl, flags, PropagationFlags.None, AccessControlType.Allow));

        if (usersMayRead)
        {
            security.AddAccessRule(new FileSystemAccessRule(
                new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
                FileSystemRights.ReadAndExecute, flags, PropagationFlags.None, AccessControlType.Allow));
        }

        info.SetAccessControl(security);
    }
}
