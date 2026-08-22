namespace EtAlii.Adp.Diagram.GcpArchitecture;

/// <summary>This diagram type's identity, cataloged in docs/diagrams.md as `gcp/architecture`.</summary>
public static class Diagram
{
    public static EtAlii.Adp.Diagram.DiagramDefinition Definition { get; } = new(
        new EtAlii.Adp.Diagram.DiagramOrigin("gcp", "architecture"),
        "GCP architecture diagram");
}
