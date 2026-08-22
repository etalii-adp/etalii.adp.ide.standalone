namespace EtAlii.Adp.Diagram.C4Dynamic;

/// <summary>This diagram type's identity, cataloged in docs/diagrams.md as `c4/dynamic`.</summary>
public static class Diagram
{
    public static EtAlii.Adp.Diagram.DiagramDefinition Definition { get; } = new(
        new EtAlii.Adp.Diagram.DiagramOrigin("c4", "dynamic"),
        "Dynamic (supplementary)");
}
