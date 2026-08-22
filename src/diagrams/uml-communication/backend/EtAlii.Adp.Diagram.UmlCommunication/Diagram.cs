namespace EtAlii.Adp.Diagram.UmlCommunication;

/// <summary>This diagram type's identity, cataloged in docs/diagrams.md as `uml/communication`.</summary>
public static class Diagram
{
    public static EtAlii.Adp.Diagram.DiagramDefinition Definition { get; } = new(
        new EtAlii.Adp.Diagram.DiagramOrigin("uml", "communication"),
        "Communication diagram");
}
