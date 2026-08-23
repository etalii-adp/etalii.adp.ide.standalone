namespace EtAlii.Adp.Diagram.Mindmap;

/// <summary>This diagram type's identity, cataloged in docs/diagrams.md as `freeplane/mindmap`.</summary>
public static class Diagram
{
    /// <summary>The Freeplane file extension; the body of a mindmap lives in a `.mm` sibling of its `.adp` registration (Requirement 2.2).</summary>
    public const string DocumentExtension = ".mm";

    public static DiagramDefinition Definition { get; } = new(
        new DiagramOrigin("freeplane", "mindmap"),
        "Mind map (radial/hierarchical, single central topic)",
        DocumentExtension);
}
