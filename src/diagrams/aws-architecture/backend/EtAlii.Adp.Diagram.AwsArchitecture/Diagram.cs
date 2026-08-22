namespace EtAlii.Adp.Diagram.AwsArchitecture;

/// <summary>This diagram type's identity, cataloged in docs/diagrams.md as `aws/architecture`.</summary>
public static class Diagram
{
    public static DiagramDefinition Definition { get; } = new(
        new DiagramOrigin("aws", "architecture"),
        "AWS architecture diagram");
}
