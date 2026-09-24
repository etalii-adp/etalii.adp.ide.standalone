using EtAlii.Adp.Documents;
namespace EtAlii.Adp.Diagram.GenericSwimlane;

/// <summary>This diagram type's identity, cataloged in docs/diagrams.md as `generic/swimlane`.</summary>
public static class Diagram
{
    public static DiagramDefinition[] Definitions { get; } =
    [
        new(
            new DiagramOrigin("generic", "swimlane"),
            "Swimlane diagram",
            "A process split into lanes, so each step is visibly somebody's responsibility.",
            Icon: "mdi-view-column-outline"),
    ];
}
