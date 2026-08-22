namespace EtAlii.Adp.Diagram.BpmnProcess;

/// <summary>This diagram type's identity, cataloged in docs/diagrams.md as `bpmn/process`.</summary>
public static class Diagram
{
    public static EtAlii.Adp.Diagram.DiagramDefinition Definition { get; } = new(
        new EtAlii.Adp.Diagram.DiagramOrigin("bpmn", "process"),
        "BPMN process diagram");
}
