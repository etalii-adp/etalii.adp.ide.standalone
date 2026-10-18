namespace EtAlii.Adp.Sessions;

/// <summary>
/// Opens the <see cref="ConnectionSession"/> of a connection with the project parts the host
/// registered: the one place a connection acquires what it and its project hold (code-structure
/// FR-067, SC-028).
/// </summary>
public sealed class ConnectionSessions(IEnumerable<IProjectSessionPart> projectParts)
{
    private readonly IReadOnlyList<IProjectSessionPart> _projectParts = [.. projectParts];

    /// <summary>Acquires what the connection watching <paramref name="root"/> as <paramref name="watchId"/> holds of its project; disposing the session releases it and all the connection held through it.</summary>
    public ConnectionSession Open(ShortGuid watchId, ProjectRoot root) =>
        ConnectionSession.Open(watchId, root, _projectParts);
}
