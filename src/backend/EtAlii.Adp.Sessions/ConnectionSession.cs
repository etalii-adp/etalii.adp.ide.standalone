namespace EtAlii.Adp.Sessions;

/// <summary>
/// Everything one connection holds, acquired as one scope and released as one: its
/// <see cref="ProjectSession"/> first, then what it holds of its own, such as the hierarchy model of
/// its watch (code-structure FR-065, FR-067). Released in the reverse order, so a connection's own
/// parts go before the project they were held in.
/// </summary>
public sealed class ConnectionSession : IDisposable
{
    private readonly HeldParts _parts;

    private ConnectionSession(ShortGuid watchId, ProjectSession project, HeldParts parts)
    {
        WatchId = watchId;
        Project = project;
        _parts = parts;
    }

    /// <summary>The watch the connection is known by.</summary>
    public ShortGuid WatchId { get; }

    /// <summary>What the connection holds of its project.</summary>
    public ProjectSession Project { get; }

    /// <summary>The project the connection has open.</summary>
    public ProjectRoot Root => Project.Root;

    /// <summary>
    /// Opens the project session, then acquires every connection part in the order given. When any
    /// acquisition fails, everything acquired before it is released, the project session included,
    /// and its exception propagates.
    /// </summary>
    public static ConnectionSession Open(
        ShortGuid watchId,
        ProjectRoot root,
        IEnumerable<IProjectSessionPart> projectParts,
        IEnumerable<IConnectionSessionPart> connectionParts)
    {
        ArgumentNullException.ThrowIfNull(connectionParts);

        // Held as the first part, so a connection part that fails releases the project with the rest.
        var project = ProjectSession.Open(root, projectParts);
        var parts = HeldParts.Acquire(
            connectionParts
                .Select(part => (Func<IDisposable>)(() => part.Acquire(watchId, root)))
                .Prepend(() => project));
        return new ConnectionSession(watchId, project, parts);
    }

    /// <summary>Releases the connection's own parts, the last acquired first, then its project session; a second call releases nothing.</summary>
    public void Dispose() => _parts.Dispose();
}
