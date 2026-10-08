namespace EtAlii.Adp.Projects;

public sealed record ProjectRecord
{
    public ShortGuid Id { get; }
    public string Name { get; }
    public PathRecord Path { get; }

    public ProjectRecord(ShortGuid id, string name, PathRecord path)
    {
        Id = id;
        Name = name;
        Path = path;
    }
}
