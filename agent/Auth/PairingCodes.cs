using System.Security.Cryptography;
using System.Text;

namespace RemoteDesktopAgent.Auth;

/// <summary>
/// Der 6-stellige Code, den der Rechner anzeigt und den man im Handy eintippt.
///
/// Sechs Ziffern sind wenig — eine Million Möglichkeiten rät man in fünf
/// Minuten durch, wenn man beliebig oft raten darf. Deshalb hängt die Sicherheit
/// hier nicht an der Länge, sondern an drei Grenzen: fünf Minuten Gültigkeit,
/// genau eine Verwendung und ein Zähler für Fehlversuche.
/// </summary>
public sealed class PairingCodes
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Nach so vielen falschen Eingaben wird der Code verworfen. Wer rät, muss
    /// den Nutzer bitten, einen neuen anzeigen zu lassen — und das fällt auf.
    /// </summary>
    public const int MaxAttempts = 5;

    private readonly TimeProvider _time;
    private readonly object _gate = new();

    private string? _code;
    private DateTimeOffset _expiresAt;
    private int _failedAttempts;

    public PairingCodes(TimeProvider time)
    {
        _time = time;
    }

    /// <summary>
    /// Erzeugt einen Code und verwirft einen eventuell noch offenen. Zwei
    /// gültige Codes gleichzeitig wären nur eine zweite Angriffsfläche — angezeigt
    /// wird ohnehin immer nur der neueste.
    /// </summary>
    public string Issue()
    {
        lock (_gate)
        {
            _code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
            _expiresAt = _time.GetUtcNow() + Lifetime;
            _failedAttempts = 0;

            return _code;
        }
    }

    /// <summary>Wie lange der offene Code noch gilt; <c>null</c> ohne offenen Code.</summary>
    public TimeSpan? RemainingLifetime()
    {
        lock (_gate)
        {
            if (_code is null)
            {
                return null;
            }

            var remaining = _expiresAt - _time.GetUtcNow();

            return remaining > TimeSpan.Zero ? remaining : null;
        }
    }

    /// <summary>
    /// Prüft den eingetippten Code und verbraucht ihn dabei. Ein zweiter Aufruf
    /// mit demselben Code schlägt fehl, auch wenn er richtig war.
    /// </summary>
    public bool TryRedeem(string presented)
    {
        lock (_gate)
        {
            if (_code is null || _time.GetUtcNow() >= _expiresAt)
            {
                _code = null;
                return false;
            }

            // Fixed-time-Vergleich: über die Laufzeit ließe sich sonst
            // stellenweise ablesen, wie weit man richtig geraten hat. Eine
            // abweichende Länge ist ohnehin falsch und wird vorher aussortiert —
            // sie verrät nichts, weil die Länge des Codes bekannt ist.
            var matches = presented.Length == _code.Length &&
                          CryptographicOperations.FixedTimeEquals(
                              Encoding.ASCII.GetBytes(presented),
                              Encoding.ASCII.GetBytes(_code));

            if (matches)
            {
                _code = null;
                return true;
            }

            if (++_failedAttempts >= MaxAttempts)
            {
                _code = null;
            }

            return false;
        }
    }
}
