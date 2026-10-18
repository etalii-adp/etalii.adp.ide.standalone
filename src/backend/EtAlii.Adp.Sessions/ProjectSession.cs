namespace EtAlii.Adp.Sessions;

/// <summary>
/// What one connection holds of the project it has open - its history and the watching of its
/// problems - acquired as one scope and released as one (code-structure FR-065, FR-067). Each part
/// keeps its own count of the connections holding it, so two connections to one project hold it twice
/// and the project lets go when the last of them does.
/// </summary>
public sealed class ProjectSession : IDisposable
{
    private readonly HeldParts _parts;

    private ProjectSession(ProjectRoot root, HeldParts parts)
    {
        Root = root;
        _parts = parts;
    }

    /// <summary>The project held.</summary>
    public ProjectRoot Root { get; }

    /// <summary>
    /// Acquires every part in the order given. When one fails, those acquired before it are released
    /// and its exception propagates, so nothing is left held.
    /// </summary>
    public static ProjectSession Open(ProjectRoot root, IEnumerable<IProjectSessionPart> parts)
    {
        ArgumentNullException.ThrowIfNull(parts);
        return new ProjectSession(root, HeldParts.Acquire(parts.Select(part => (Func<IDisposable>)(() => part.Acquire(root)))));
    }

    /// <summary>Releases every part, the last acquired first; a second call releases nothing.</summary>
    public void Dispose() => _parts.Dispose();
}
