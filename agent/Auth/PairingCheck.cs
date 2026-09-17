namespace RemoteDesktopAgent.Auth;

/// <summary>
/// Das Prüfzeichen neben dem Kopplungscode: die ersten acht Hexstellen des
/// CA-Fingerabdrucks. Es steht hier und nicht in der App allein, weil beide
/// Seiten dieselbe Länge meinen müssen — sonst passt nie etwas.
/// </summary>
public static class PairingCheck
{
    public const int Length = 8;

    public static string Of(string caFingerprint) =>
        caFingerprint.Trim().ToLowerInvariant()[..Math.Min(Length, caFingerprint.Trim().Length)];
}
