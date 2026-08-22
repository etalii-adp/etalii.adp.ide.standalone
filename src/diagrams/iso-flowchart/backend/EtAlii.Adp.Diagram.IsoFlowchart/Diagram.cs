namespace EtAlii.Adp.Diagram.IsoFlowchart;

/// <summary>This diagram type's identity, cataloged in docs/diagrams.md as `iso/flowchart`.</summary>
public static class Diagram
{
    public static DiagramDefinition Definition { get; } = new(
        new DiagramOrigin("iso", "flowchart"),
        "Flowchart");
}
