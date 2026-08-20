namespace EtAlii.Adp.Backend.Projects;

public sealed record ProjectRecord(string Id, string Name, IReadOnlyList<string> PathSegments);

public interface IProjectStore
{
    IReadOnlyList<ProjectRecord> List(string userId);

    /// <exception cref="InvalidProjectPathException">The path does not resolve to an accessible folder.</exception>
    ProjectRecord Add(string userId, string name, IReadOnlyList<string> pathSegments);

    void Remove(string userId, string projectId);
}
