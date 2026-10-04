namespace EtAlii.Adp.Diagram.SupplyChain;

/// <summary>
/// What each connection has selected in each supply chain diagram - the one thing the trace
/// highlight needs that the document does not hold.
/// </summary>
/// <remarks>
/// <para>
/// <b>Selection stays the backend's, and this is where the module learns of it.</b> The client
/// library owns the gesture and the context service owns the record; a module client may not read
/// either. The context service tells the resolver that resolved a selection by calling its
/// <c>Track</c>, and disposes what that returned when the selection moves on. The resolver records
/// both here, and the session on the same connection and body re-renders with the chain marked - so
/// the highlight reaches the canvas as ordinary deltas, like any other change.
/// </para>
/// <para>
/// <b>Keyed by connection and body</b>, because one browser tab is one connection with several
/// diagrams open, and a selection in one must not light up another.
/// </para>
/// </remarks>
public sealed class SupplyChainSelections
{
    private readonly Lock _gate = new();
    private readonly Dictionary<(ShortGuid WatchId, string BodyPath), (string ElementId, long Token)> _selected = [];
    private long _next;

    /// <summary>Raised after the selection on a connection and body changed.</summary>
    public event EventHandler<SupplyChainSelectionChangedEventArgs>? Changed;

    /// <summary>The element selected on <paramref name="watchId"/> in <paramref name="bodyPath"/>, or <c>null</c>.</summary>
    public string? Of(ShortGuid watchId, string bodyPath)
    {
        lock (_gate)
        {
            return _selected.TryGetValue((watchId, Key(bodyPath)), out var entry) ? entry.ElementId : null;
        }
    }

    /// <summary>Records a selection, until the returned handle is disposed.</summary>
    public IDisposable Select(ShortGuid watchId, string bodyPath, string elementId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bodyPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(elementId);

        long token;
        lock (_gate)
        {
            token = ++_next;
            _selected[(watchId, Key(bodyPath))] = (elementId, token);
        }

        Raise(watchId, bodyPath);
        return new Release(() => Deselect(watchId, bodyPath, token));
    }

    /// <summary>
    /// Clears a selection - but only the one this handle recorded. The service records the next
    /// selection before it disposes the previous one, so clearing by key would erase the new one.
    /// </summary>
    private void Deselect(ShortGuid watchId, string bodyPath, long token)
    {
        lock (_gate)
        {
            if (!_selected.TryGetValue((watchId, Key(bodyPath)), out var entry) || entry.Token != token)
            {
                return;
            }

            _selected.Remove((watchId, Key(bodyPath)));
        }

        Raise(watchId, bodyPath);
    }

    private void Raise(ShortGuid watchId, string bodyPath) =>
        Changed?.Invoke(this, new SupplyChainSelectionChangedEventArgs(watchId, bodyPath));

    private static string Key(string bodyPath) => bodyPath.ToUpperInvariant();

    private sealed class Release(Action release) : IDisposable
    {
        private Action? _release = release;

        public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
    }
}

/// <summary>The selection on <paramref name="watchId"/> in <paramref name="bodyPath"/> changed.</summary>
public sealed class SupplyChainSelectionChangedEventArgs(ShortGuid watchId, string bodyPath) : EventArgs
{
    public ShortGuid WatchId { get; } = watchId;

    public string BodyPath { get; } = bodyPath;
}
