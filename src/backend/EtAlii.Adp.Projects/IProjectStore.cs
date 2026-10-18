namespace EtAlii.Adp.Projects;

public interface IProjectStore
{
    IReadOnlyList<ProjectRecord> List(ShortGuid userId);

    /// <exception cref="InvalidProjectPathException">The path does not resolve to an accessible folder.</exception>
    Task<ProjectRecord> AddAsync(ShortGuid userId, string name, PathRecord path, CancellationToken cancellationToken);

    Task RemoveAsync(ShortGuid userId, ShortGuid projectId, CancellationToken cancellationToken);
}
