namespace EtAlii.Adp.Sessions;

/// <summary>
/// One thing a connection holds of the project it has open - the project's history, the watching
/// of its problems - acquired by the connection's <see cref="ProjectSession"/> and released with it.
/// </summary>
/// <remarks>
/// Declared here and implemented by the project that owns the part, wherever the sessions project
/// referencing that project would close a cycle (code-structure FR-065, FR-067): the problems and
/// the hierarchy reference the sessions project, so it cannot reference them.
/// </remarks>
public interface IProjectSessionPart
{
    /// <summary>Acquires the part for one connection to <paramref name="root"/>; disposing what is returned releases it.</summary>
    IDisposable Acquire(ProjectRoot root);
}
