namespace EtAlii.Adp.Diagram.DddContextMap;

/// <summary>This diagram type's identity, cataloged in docs/diagrams.md as `ddd/context-map`.</summary>
public static class Diagram
{
    public static EtAlii.Adp.Diagram.DiagramDefinition Definition { get; } = new(
        new EtAlii.Adp.Diagram.DiagramOrigin("ddd", "context-map"),
        "Bounded Context / Context Map");
}
