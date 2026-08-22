namespace EtAlii.Adp.Diagram.UmlSequence;

/// <summary>This diagram type's identity, cataloged in docs/diagrams.md as `uml/sequence`.</summary>
public static class Diagram
{
    public static EtAlii.Adp.Diagram.DiagramDefinition Definition { get; } = new(
        new EtAlii.Adp.Diagram.DiagramOrigin("uml", "sequence"),
        "Sequence diagram");
}
