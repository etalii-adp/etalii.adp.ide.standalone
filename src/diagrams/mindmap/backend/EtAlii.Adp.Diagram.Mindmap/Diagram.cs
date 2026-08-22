namespace EtAlii.Adp.Diagram.Mindmap;

/// <summary>This diagram type's identity, cataloged in docs/diagrams.md as `freeplane/mindmap`.</summary>
public static class Diagram
{
    public static DiagramDefinition Definition { get; } = new(
        new DiagramOrigin("freeplane", "mindmap"),
        "Mind map (radial/hierarchical, single central topic)");
}
