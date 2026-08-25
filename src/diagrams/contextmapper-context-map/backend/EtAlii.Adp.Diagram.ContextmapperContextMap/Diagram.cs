namespace EtAlii.Adp.Diagram.ContextmapperContextMap;

/// <summary>This diagram type's identity, cataloged in docs/diagrams.md as `contextmapper/context-map`.</summary>
public static class Diagram
{
    public static DiagramDefinition[] Definitions { get; } =
    [
        new(
            new DiagramOrigin("contextmapper", "context-map"),
            "DDD Context Map, plus generated PlantUML/BPMN sketches",
            "Bounded contexts and the strategic relationships between the teams that own them."),
    ];
}
