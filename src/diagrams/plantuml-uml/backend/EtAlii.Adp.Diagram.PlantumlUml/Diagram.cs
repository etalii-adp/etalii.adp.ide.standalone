namespace EtAlii.Adp.Diagram.PlantumlUml;

/// <summary>This diagram type's identity, cataloged in docs/diagrams.md as `plantuml/uml`.</summary>
public static class Diagram
{
    public static EtAlii.Adp.Diagram.DiagramDefinition Definition { get; } = new(
        new EtAlii.Adp.Diagram.DiagramOrigin("plantuml", "uml"),
        "Full UML set (see section 1)");
}
