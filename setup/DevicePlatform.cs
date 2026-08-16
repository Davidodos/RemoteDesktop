namespace RemoteDesktopSetup;

/// <summary>
/// Was ein Gerät ist — ein Rechner oder ein Handy.
///
/// <para>
/// Die Angabe steht in <c>/api/info</c> **und** im Steckbrief, und das ist
/// Absicht: die Geräteliste soll das Symbol auch dann zeigen, wenn das Gerät
/// gerade aus ist. Aus <c>/api/info</c> allein käme sie nur von einem Gerät,
/// das antwortet.
/// </para>
///
/// <para>
/// Ein Gerät ohne dieses Feld ist älter als Phase 31g. Dann steht in der Liste
/// nichts, statt „Windows" zu raten — geraten würde genau in dem Fall falsch,
/// in dem es darauf ankommt.
/// </para>
///
/// <para>
/// Nicht zu verwechseln mit <c>capabilities</c>: die sagen, was ein Gerät kann,
/// und danach richtet sich die Oberfläche. Was es <em>ist</em>, entscheidet
/// nur, welches Symbol daneben steht.
/// </para>
/// </summary>
public static class DevicePlatform
{
    public const string Windows = "windows";
    public const string Android = "android";
}
