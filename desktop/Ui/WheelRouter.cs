using System.Runtime.InteropServices;

namespace RemoteDesktopClient.Ui;

/// <summary>
/// Leitet das Mausrad an das Element unter dem Zeiger.
///
/// <para>
/// **Der Grund:** Windows schickt <c>WM_MOUSEWHEEL</c> an das Fenster mit dem
/// Tastaturfokus, nicht an das unter dem Zeiger. In einem Fenster mit
/// Seitenleiste heißt das: wer eben auf „Netz" geklickt hat, hat den Fokus in
/// der Leiste — und die Seite daneben ließe sich mit dem Rad nicht rollen,
/// obwohl der Zeiger darüber steht. Jeder erwartet das Gegenteil.
/// </para>
///
/// <para>
/// Alles, was nicht in einem rollbaren Stapel liegt, bleibt unberührt und geht
/// den gewöhnlichen Weg — insbesondere die WebView: deren Fenster gehört
/// Chromium, <see cref="Control.FromHandle"/> findet dazu kein Steuerelement,
/// und das Rollen im Fernsteuerbild bleibt das der ferngesteuerten Seite.
/// </para>
/// </summary>
public sealed class WheelRouter : IMessageFilter
{
    private const int MouseWheel = 0x020A;

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(Point point);

    public bool PreFilterMessage(ref Message message)
    {
        if (message.Msg != MouseWheel)
        {
            return false;
        }

        // Bei dieser Nachricht stecken die Bildschirmkoordinaten als zwei
        // vorzeichenbehaftete Kurzzahlen in lParam, die Drehung in der oberen
        // Hälfte von wParam. Ohne die Umdeutung auf `short` würde ein zweiter
        // Bildschirm links vom ersten zu Koordinaten weit jenseits des
        // Sichtbaren.
        var packed = message.LParam.ToInt64();

        var under = Control.FromHandle(
            WindowFromPoint(new Point((short)(packed & 0xFFFF), (short)((packed >> 16) & 0xFFFF))));

        var delta = (short)((message.WParam.ToInt64() >> 16) & 0xFFFF);

        for (var control = under; control is not null; control = control.Parent)
        {
            if (control is Stack stack && stack.Wheel(delta))
            {
                return true;
            }
        }

        return false;
    }
}
