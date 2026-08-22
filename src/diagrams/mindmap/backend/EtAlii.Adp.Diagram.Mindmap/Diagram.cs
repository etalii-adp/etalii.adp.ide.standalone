namespace EtAlii.Adp.Diagram.Mindmap;

/// <summary>This diagram type's identity, cataloged in docs/diagrams.md as `freeplane/mindmap`.</summary>
public static class Diagram
{
    public static EtAlii.Adp.Diagram.DiagramDefinition Definition { get; } = new(
        new EtAlii.Adp.Diagram.DiagramOrigin("freeplane", "mindmap"),
        "Mind map (radial/hierarchical, single central topic)");
}
