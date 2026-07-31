using System.Diagnostics;
using RemoteDesktopAgent.Native;

namespace RemoteDesktopAgent.Actions;

/// <summary>
/// Alles, was der <see cref="ActionRunner"/> außerhalb seiner selbst anfasst.
///
/// Als eigene Schnittstelle, damit die Prüfungen belegen können, <b>wie</b>
/// gestartet wird — dass die Argumente einzeln übergeben werden und nie über
/// eine Shell. Genau das ist die Zusage, an der hier alles hängt; sie muss
/// prüfbar sein und nicht nur behauptet.
/// </summary>
public interface IActionHost
{
    void Start(ProcessStartInfo start);

    void KeyDown(ushort virtualKey);

    void KeyUp(ushort virtualKey);
}

/// <summary>Die Umsetzung für den laufenden Windows-Rechner.</summary>
public sealed class WindowsActionHost : IActionHost
{
    private readonly InputSender _input;

    public WindowsActionHost(InputSender input)
    {
        _input = input;
    }

    public void Start(ProcessStartInfo start) => Process.Start(start);

    public void KeyDown(ushort virtualKey) => _input.KeyDown(virtualKey);

    public void KeyUp(ushort virtualKey) => _input.KeyUp(virtualKey);
}
