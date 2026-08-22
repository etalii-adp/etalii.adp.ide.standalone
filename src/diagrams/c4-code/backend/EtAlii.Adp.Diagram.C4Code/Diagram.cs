namespace EtAlii.Adp.Diagram.C4Code;

/// <summary>This diagram type's identity, cataloged in docs/diagrams.md as `c4/code`.</summary>
public static class Diagram
{
    public static EtAlii.Adp.Diagram.DiagramDefinition Definition { get; } = new(
        new EtAlii.Adp.Diagram.DiagramOrigin("c4", "code"),
        "Code (optional)");
}
