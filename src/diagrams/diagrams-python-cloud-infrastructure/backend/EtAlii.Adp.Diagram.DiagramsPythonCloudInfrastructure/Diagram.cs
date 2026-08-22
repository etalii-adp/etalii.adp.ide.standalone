namespace EtAlii.Adp.Diagram.DiagramsPythonCloudInfrastructure;

/// <summary>This diagram type's identity, cataloged in docs/diagrams.md as `diagrams-python/cloud-infrastructure`.</summary>
public static class Diagram
{
    public static DiagramDefinition Definition { get; } = new(
        new DiagramOrigin("diagrams-python", "cloud-infrastructure"),
        "Cloud/infrastructure diagram with official-style vendor icons");
}
