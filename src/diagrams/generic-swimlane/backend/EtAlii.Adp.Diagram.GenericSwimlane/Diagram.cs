namespace EtAlii.Adp.Diagram.GenericSwimlane;

/// <summary>This diagram type's identity, cataloged in docs/diagrams.md as `generic/swimlane`.</summary>
public static class Diagram
{
    public static EtAlii.Adp.Diagram.DiagramDefinition Definition { get; } = new(
        new EtAlii.Adp.Diagram.DiagramOrigin("generic", "swimlane"),
        "Swimlane diagram");
}
