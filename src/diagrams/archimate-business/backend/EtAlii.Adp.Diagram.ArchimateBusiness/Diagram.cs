namespace EtAlii.Adp.Diagram.ArchimateBusiness;

/// <summary>This diagram type's identity, cataloged in docs/diagrams.md as `archimate/business`.</summary>
public static class Diagram
{
    public static EtAlii.Adp.Diagram.DiagramDefinition Definition { get; } = new(
        new EtAlii.Adp.Diagram.DiagramOrigin("archimate", "business"),
        "ArchiMate — Business layer");
}
