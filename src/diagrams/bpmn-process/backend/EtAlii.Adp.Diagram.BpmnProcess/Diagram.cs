namespace EtAlii.Adp.Diagram.BpmnProcess;

/// <summary>This diagram type's identity, cataloged in docs/diagrams.md as `bpmn/process`.</summary>
public static class Diagram
{
    public static DiagramDefinition[] Definitions { get; } =
    [
        new(
            new DiagramOrigin("bpmn", "process"),
            "BPMN process diagram",
            "A business process as an executable flow: events, tasks, gateways and the lanes that own them.",
            Icon: "mdi-arrow-decision"),
    ];
}
