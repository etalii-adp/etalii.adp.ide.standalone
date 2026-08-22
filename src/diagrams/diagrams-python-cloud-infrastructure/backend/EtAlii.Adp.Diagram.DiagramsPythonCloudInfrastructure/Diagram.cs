namespace EtAlii.Adp.Diagram.DiagramsPythonCloudInfrastructure;

/// <summary>This diagram type's identity, cataloged in docs/diagrams.md as `diagrams-python/cloud-infrastructure`.</summary>
public static class Diagram
{
    public static EtAlii.Adp.Diagram.DiagramDefinition Definition { get; } = new(
        new EtAlii.Adp.Diagram.DiagramOrigin("diagrams-python", "cloud-infrastructure"),
        "Cloud/infrastructure diagram with official-style vendor icons");
}
