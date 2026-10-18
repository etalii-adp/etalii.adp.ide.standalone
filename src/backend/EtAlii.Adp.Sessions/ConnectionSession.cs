namespace EtAlii.Adp.Sessions;

/// <summary>
/// Everything one connection holds, acquired through one scope and released as one: its
/// <see cref="ProjectSession"/> first, then what each part of the connection holds of its own as it
/// starts - the hierarchy model of its watch, its registration on the context stream, its place
/// among the workspace connections (code-structure FR-065, FR-067). Released in the reverse order,
/// so a connection's own parts go before the project they were held in, and only here: whoever
/// acquires through the scope releases nothing itself.
/// </summary>
public sealed class ConnectionSession : IDisposable
{
    private readonly HeldParts _parts;

    private ConnectionSession(ShortGuid watchId, ProjectRoot root, HeldParts parts)
    {
        WatchId = watchId;
        Root = root;
        _parts = parts;
    }

    /// <summary>The watch the connection is known by.</summary>
    public ShortGuid WatchId { get; }

    /// <summary>The project the connection has open.</summary>
    public ProjectRoot Root { get; }

    /// <summary>
    /// Opens the project session, acquiring every project part in the order given. When an
    /// acquisition fails, everything acquired before it is released and its exception propagates.
    /// </summary>
    public static ConnectionSession Open(ShortGuid watchId, ProjectRoot root, IEnumerable<IProjectSessionPart> projectParts)
    {
        // Held as the first part, so the project is released last, after everything held later.
        var project = ProjectSession.Open(root, projectParts);
        return new ConnectionSession(watchId, root, HeldParts.Acquire([() => project]));
    }

    /// <summary>
    /// Acquires what a part of the connection holds of its own, released with the session and before
    /// what was held earlier. Once the session is released, what is acquired is released at once and
    /// <see cref="ObjectDisposedException"/> is thrown.
    /// </summary>
    public T Hold<T>(Func<T> acquire)
        where T : IDisposable =>
        _parts.Hold(acquire);

    /// <summary>Releases what the connection holds, the last acquired first, then its project session; a second call releases nothing.</summary>
    public void Dispose() => _parts.Dispose();
}
