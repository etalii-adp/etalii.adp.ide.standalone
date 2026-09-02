namespace EtAlii.Adp.Diagram.IsoFlowchart;

/// <summary>This diagram type's identity, cataloged in docs/diagrams.md as `iso/flowchart`.</summary>
public static class Diagram
{
    public static DiagramDefinition[] Definitions { get; } =
    [
        new(
            new DiagramOrigin("iso", "flowchart"),
            "Flowchart",
            "A procedure as a flow of steps and decisions, in the ISO 5807 shapes everyone recognises.",
            Icon: "mdi-ray-start-arrow"),
    ];
}
