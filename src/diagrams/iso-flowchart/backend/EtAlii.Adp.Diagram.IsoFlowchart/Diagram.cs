namespace EtAlii.Adp.Diagram.IsoFlowchart;

/// <summary>This diagram type's identity, cataloged in docs/diagrams.md as `iso/flowchart`.</summary>
public static class Diagram
{
    public static EtAlii.Adp.Diagram.DiagramDefinition Definition { get; } = new(
        new EtAlii.Adp.Diagram.DiagramOrigin("iso", "flowchart"),
        "Flowchart");
}
