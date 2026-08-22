namespace EtAlii.Adp.Diagram.C4Dynamic;

/// <summary>This diagram type's identity, cataloged in docs/diagrams.md as `c4/dynamic`.</summary>
public static class Diagram
{
    public static DiagramDefinition Definition { get; } = new(
        new DiagramOrigin("c4", "dynamic"),
        "Dynamic (supplementary)");
}
