using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace RemoteDesktopAgent.Api;

/// <summary>
/// Der Eingabe-WebSocket. Getrennt vom späteren Video-Stream, damit die
/// Eingabe-Latenz nie an einem vollen Bild-Puffer hängt.
/// </summary>
public sealed class InputSocket(InputExecutor executor, ILogger<InputSocket> logger)
{
    /// <summary>Reicht für jeden Befehl; 'text' ist auf 4096 Zeichen begrenzt.</summary>
    private const int ReceiveBufferSize = 16 * 1024;

    public async Task HandleAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[ReceiveBufferSize];
        var heldKeys = new HeldInputTracker(executor, logger);

        try
        {
            while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                var message = await ReceiveMessageAsync(socket, buffer, cancellationToken);

                if (message is null)
                {
                    break;
                }

                await ProcessAsync(socket, message, heldKeys, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Normaler Shutdown.
        }
        catch (WebSocketException ex)
        {
            logger.LogInformation("Input-Socket getrennt: {Message}", ex.Message);
        }
        finally
        {
            // Kritisch: Bricht die Verbindung ab, während eine Taste oder
            // Maustaste gedrückt gehalten wird, bliebe sie es für immer.
            heldKeys.ReleaseAll();
        }
    }

    private async Task ProcessAsync(
        WebSocket socket,
        string message,
        HeldInputTracker heldKeys,
        CancellationToken cancellationToken)
    {
        var result = InputCommandParser.Parse(message);

        if (!result.IsSuccess)
        {
            logger.LogWarning("Ungültiger Eingabe-Befehl: {Error}", result.Error);
            await SendErrorAsync(socket, result.Error!, cancellationToken);
            return;
        }

        try
        {
            executor.Execute(result.Command!);
            heldKeys.Track(result.Command!);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Eingabe-Befehl fehlgeschlagen: {Command}", result.Command);
            await SendErrorAsync(socket, ex.Message, cancellationToken);
        }
    }

    private static async Task<string?> ReceiveMessageAsync(
        WebSocket socket, byte[] buffer, CancellationToken cancellationToken)
    {
        var received = await socket.ReceiveAsync(
            new ArraySegment<byte>(buffer), cancellationToken);

        if (received.MessageType == WebSocketMessageType.Close)
        {
            await socket.CloseAsync(
                WebSocketCloseStatus.NormalClosure, null, cancellationToken);
            return null;
        }

        if (!received.EndOfMessage)
        {
            // Alle Befehle passen in einen Frame. Ein fragmentierter Frame
            // heißt: hier stimmt etwas nicht.
            return null;
        }

        return Encoding.UTF8.GetString(buffer, 0, received.Count);
    }

    private static async Task SendErrorAsync(
        WebSocket socket, string error, CancellationToken cancellationToken)
    {
        if (socket.State != WebSocketState.Open)
        {
            return;
        }

        var payload = JsonSerializer.SerializeToUtf8Bytes(new { t = "error", message = error });

        await socket.SendAsync(
            payload, WebSocketMessageType.Text, endOfMessage: true, cancellationToken);
    }
}

/// <summary>
/// Merkt sich, was gerade gedrückt gehalten wird, um es beim Verbindungsabbruch
/// wieder loszulassen.
///
/// Ohne das bleibt nach einem Abbruch mitten im Drag die linke Maustaste
/// gedrückt oder Alt hängt — der PC ist dann faktisch unbedienbar, bis jemand
/// physisch eingreift.
/// </summary>
internal sealed class HeldInputTracker(InputExecutor executor, ILogger logger)
{
    private readonly HashSet<Native.MouseButton> _buttons = [];
    private readonly HashSet<ushort> _keys = [];

    public void Track(InputCommand command)
    {
        switch (command)
        {
            case InputCommand.ButtonDown down:
                _buttons.Add(down.Button);
                break;

            case InputCommand.ButtonUp up:
                _buttons.Remove(up.Button);
                break;

            case InputCommand.KeyDown keyDown:
                _keys.Add(keyDown.VirtualKey);
                break;

            case InputCommand.KeyUp keyUp:
                _keys.Remove(keyUp.VirtualKey);
                break;
        }
    }

    public void ReleaseAll()
    {
        if (_buttons.Count == 0 && _keys.Count == 0)
        {
            return;
        }

        logger.LogWarning(
            "Verbindung weg — löse {Buttons} Maustaste(n) und {Keys} Taste(n).",
            _buttons.Count, _keys.Count);

        foreach (var button in _buttons)
        {
            TryRelease(() => executor.Execute(new InputCommand.ButtonUp(button)));
        }

        foreach (var key in _keys)
        {
            TryRelease(() => executor.Execute(new InputCommand.KeyUp(key)));
        }

        _buttons.Clear();
        _keys.Clear();
    }

    private void TryRelease(Action release)
    {
        try
        {
            release();
        }
        catch (Exception ex)
        {
            // Jede Taste einzeln versuchen — eine fehlgeschlagene darf die
            // anderen nicht blockieren.
            logger.LogError(ex, "Loslassen fehlgeschlagen.");
        }
    }
}
