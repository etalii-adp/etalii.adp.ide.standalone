namespace EtAlii.Adp.Diagram.AzureArchitecture;

/// <summary>This diagram type's identity, cataloged in docs/diagrams.md as `azure/architecture`.</summary>
public static class Diagram
{
    public static EtAlii.Adp.Diagram.DiagramDefinition Definition { get; } = new(
        new EtAlii.Adp.Diagram.DiagramOrigin("azure", "architecture"),
        "Azure architecture diagram");
}
