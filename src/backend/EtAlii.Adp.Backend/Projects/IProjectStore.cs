namespace EtAlii.Adp.Backend.Projects;

public sealed record ProjectRecord(ShortGuid Id, string Name, IReadOnlyList<string> PathSegments);

public interface IProjectStore
{
    IReadOnlyList<ProjectRecord> List(ShortGuid userId);

    /// <exception cref="InvalidProjectPathException">The path does not resolve to an accessible folder.</exception>
    ProjectRecord Add(ShortGuid userId, string name, IReadOnlyList<string> pathSegments);

    void Remove(ShortGuid userId, ShortGuid projectId);
}
