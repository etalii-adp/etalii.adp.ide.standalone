namespace EtAlii.Adp.Backend.Projects;

public sealed record ProjectRecord(
    ShortGuid Id,
    string Name,
    IReadOnlyList<string> PathSegments);
