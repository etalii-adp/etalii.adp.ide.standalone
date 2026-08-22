namespace EtAlii.Adp.Diagram.AzureArchitecture;

/// <summary>This diagram type's identity, cataloged in docs/diagrams.md as `azure/architecture`.</summary>
public static class Diagram
{
    public static DiagramDefinition Definition { get; } = new(
        new DiagramOrigin("azure", "architecture"),
        "Azure architecture diagram");
}
