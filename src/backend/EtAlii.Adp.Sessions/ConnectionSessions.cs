namespace EtAlii.Adp.Sessions;

/// <summary>
/// Opens the <see cref="ConnectionSession"/> of a connection with the parts the host registered: the
/// one place a connection acquires what it and its project hold (code-structure FR-067, SC-028).
/// </summary>
public sealed class ConnectionSessions(IEnumerable<IProjectSessionPart> projectParts, IEnumerable<IConnectionSessionPart> connectionParts)
{
    private readonly IReadOnlyList<IProjectSessionPart> _projectParts = [.. projectParts];
    private readonly IReadOnlyList<IConnectionSessionPart> _connectionParts = [.. connectionParts];

    /// <summary>Acquires everything the connection watching <paramref name="root"/> as <paramref name="watchId"/> holds; disposing the session releases it.</summary>
    public ConnectionSession Open(ShortGuid watchId, ProjectRoot root) =>
        ConnectionSession.Open(watchId, root, _projectParts, _connectionParts);
}
