namespace EtAlii.Adp.Diagram.ContextmapperContextMap;

/// <summary>This diagram type's identity, cataloged in docs/diagrams.md as `contextmapper/context-map`.</summary>
public static class Diagram
{
    public static EtAlii.Adp.Diagram.DiagramDefinition Definition { get; } = new(
        new EtAlii.Adp.Diagram.DiagramOrigin("contextmapper", "context-map"),
        "DDD Context Map, plus generated PlantUML/BPMN sketches");
}
