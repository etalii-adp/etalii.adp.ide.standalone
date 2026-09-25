using EtAlii.Adp.Documents;
namespace EtAlii.Adp.Diagram.DiagramsPythonCloudInfrastructure;

/// <summary>This diagram type's identity, cataloged in docs/diagrams.md as `diagrams-python/cloud-infrastructure`.</summary>
public static class Diagram
{
    public static DiagramDefinition[] Definitions { get; } =
    [
        new(
            new DiagramOrigin("diagrams-python", "cloud-infrastructure"),
            "Cloud/infrastructure diagram with official-style vendor icons",
            "Cloud infrastructure drawn as code with the Diagrams Python library.",
            Icon: "mdi-cloud-outline"),
    ];
}
