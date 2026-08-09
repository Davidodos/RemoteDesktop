namespace RemoteDesktopAgent.Auth;

/// <summary>
/// Die offenen Dauerverbindungen, nach gekoppeltem Gerät sortiert.
///
/// <para>
/// **Der Befund dahinter:** ein widerrufenes Gerät verlor sofort seine Sitzung
/// (<see cref="SessionStore.CloseAll"/>) und bekam auf den nächsten REST-Aufruf
/// auch prompt eine Fehlermeldung — behielt aber Bild und Eingabe, bis jemand
/// die App auf dem Handy schloss. Der Grund: die beiden WebSockets werden genau
/// einmal geprüft, nämlich beim Verbinden. Danach fragt niemand mehr, und eine
/// bestehende Verbindung überdauert damit ihre eigene Berechtigung. Ein
/// Widerruf, der die Fernsteuerung weiterlaufen lässt, ist keiner.
/// </para>
///
/// <para>
/// Deshalb hinterlegt jede Dauerverbindung hier eine Abrisskante. Wird das Gerät
/// widerrufen, fällt sie — und die Verbindung endet in dem Moment, in dem der
/// Eintrag verschwindet, nicht Stunden später.
/// </para>
/// </summary>
public sealed class LiveConnections
{
    private readonly object _gate = new();
    private readonly Dictionary<string, List<CancellationTokenSource>> _open = [];

    /// <summary>
    /// Meldet eine Dauerverbindung an. Das Ergebnis muss beim Ende der
    /// Verbindung entsorgt werden, sonst wächst die Liste mit jedem Aufruf.
    /// </summary>
    /// <param name="clientId">
    /// Wem die Verbindung gehört — <c>null</c> beim alten Sammel-Token, das kein
    /// Gerät kennt und deshalb auch nicht einzeln widerrufen werden kann.
    /// </param>
    /// <param name="request">
    /// Der Abbruch der Anfrage. Er wird mitverdrahtet, damit der Aufrufer nur
    /// noch einen einzigen Token braucht.
    /// </param>
    public Lease Open(string? clientId, CancellationToken request)
    {
        var source = CancellationTokenSource.CreateLinkedTokenSource(request);

        if (clientId is null)
        {
            return new Lease(this, null, source);
        }

        lock (_gate)
        {
            if (!_open.TryGetValue(clientId, out var list))
            {
                _open[clientId] = list = [];
            }

            list.Add(source);
        }

        return new Lease(this, clientId, source);
    }

    /// <summary>
    /// Trennt alles, was dieses Gerät gerade offen hält.
    /// </summary>
    /// <returns>Wie viele Verbindungen getrennt wurden.</returns>
    public int Close(string clientId)
    {
        List<CancellationTokenSource> closing;

        lock (_gate)
        {
            if (!_open.Remove(clientId, out var list))
            {
                return 0;
            }

            closing = list;
        }

        // Ausdrücklich außerhalb der Sperre: das Abbrechen ruft die
        // Rückmeldungen der Verbindungen auf, und die räumen ihrerseits hier
        // auf. Innerhalb der Sperre wäre das ein Griff in das eigene Schloss.
        foreach (var source in closing)
        {
            try
            {
                source.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Die Verbindung war schon von selbst zu Ende. Genau das
                // Ergebnis, das hier gewollt ist.
            }
        }

        return closing.Count;
    }

    /// <summary>Wie viele Dauerverbindungen dieses Gerät gerade offen hat.</summary>
    public int CountFor(string clientId)
    {
        lock (_gate)
        {
            return _open.TryGetValue(clientId, out var list) ? list.Count : 0;
        }
    }

    private void Release(string? clientId, CancellationTokenSource source)
    {
        if (clientId is not null)
        {
            lock (_gate)
            {
                if (_open.TryGetValue(clientId, out var list)
                    && list.Remove(source)
                    && list.Count == 0)
                {
                    _open.Remove(clientId);
                }
            }
        }

        source.Dispose();
    }

    /// <summary>Die Anmeldung einer Dauerverbindung, solange sie steht.</summary>
    public sealed class Lease : IDisposable
    {
        private readonly LiveConnections _owner;
        private readonly string? _clientId;
        private readonly CancellationTokenSource _source;

        private bool _released;

        internal Lease(LiveConnections owner, string? clientId, CancellationTokenSource source)
        {
            _owner = owner;
            _clientId = clientId;
            _source = source;
        }

        /// <summary>
        /// Endet, wenn die Anfrage abbricht **oder** das Gerät widerrufen wird.
        /// Die Verbindung soll auf beides gleich reagieren.
        /// </summary>
        public CancellationToken Token => _source.Token;

        public void Dispose()
        {
            if (_released)
            {
                return;
            }

            _released = true;
            _owner.Release(_clientId, _source);
        }
    }
}
