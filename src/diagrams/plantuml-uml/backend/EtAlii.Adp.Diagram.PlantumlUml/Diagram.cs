namespace EtAlii.Adp.Diagram.PlantumlUml;

/// <summary>This diagram type's identity, cataloged in docs/diagrams.md as `plantuml/uml`.</summary>
public static class Diagram
{
    public static DiagramDefinition Definition { get; } = new(
        new DiagramOrigin("plantuml", "uml"),
        "Full UML set (see section 1)",
        "UML written as PlantUML text and rendered from it.");
}
