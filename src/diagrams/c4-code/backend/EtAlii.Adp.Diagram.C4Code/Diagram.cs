namespace EtAlii.Adp.Diagram.C4Code;

/// <summary>This diagram type's identity, cataloged in docs/diagrams.md as `c4/code`.</summary>
public static class Diagram
{
    public static DiagramDefinition Definition { get; } = new(
        new DiagramOrigin("c4", "code"),
        "Code (optional)");
}
