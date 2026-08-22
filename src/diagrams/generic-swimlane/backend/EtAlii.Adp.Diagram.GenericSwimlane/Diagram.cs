namespace EtAlii.Adp.Diagram.GenericSwimlane;

/// <summary>This diagram type's identity, cataloged in docs/diagrams.md as `generic/swimlane`.</summary>
public static class Diagram
{
    public static DiagramDefinition Definition { get; } = new(
        new DiagramOrigin("generic", "swimlane"),
        "Swimlane diagram");
}
