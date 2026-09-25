using EtAlii.Adp.Documents;
namespace EtAlii.Adp.Diagram.GcpArchitecture;

/// <summary>This diagram type's identity, cataloged in docs/diagrams.md as `gcp/architecture`.</summary>
public static class Diagram
{
    public static DiagramDefinition[] Definitions { get; } =
    [
        new(
            new DiagramOrigin("gcp", "architecture"),
            "GCP architecture diagram",
            "How a solution is built out of Google Cloud services, drawn with the vendor's own icon set.",
            Icon: "mdi-google-cloud"),
    ];
}
