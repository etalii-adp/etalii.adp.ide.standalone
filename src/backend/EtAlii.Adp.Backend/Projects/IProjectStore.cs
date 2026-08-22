namespace EtAlii.Adp.Backend.Projects;

public interface IProjectStore
{
    IReadOnlyList<ProjectRecord> List(ShortGuid userId);

    /// <exception cref="InvalidProjectPathException">The path does not resolve to an accessible folder.</exception>
    ProjectRecord Add(ShortGuid userId, string name, PathRecord path);

    void Remove(ShortGuid userId, ShortGuid projectId);
}
