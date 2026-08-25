namespace EtAlii.Adp.Diagram.Mindmap;

/// <summary>This diagram type's identity, cataloged in docs/diagrams.md as `freeplane/mindmap`.</summary>
public static class Diagram
{
    /// <summary>The Freeplane file extension; the body of a mindmap lives in a `.mm` sibling of its `.adp` registration (Requirement 2.2).</summary>
    public const string DocumentExtension = ".mm";

    /// <summary>
    /// This module's one type, named so the module's own registrations can say which type they
    /// serve without indexing into the array. Discovery reads <see cref="Definitions"/>; the
    /// module reads this.
    /// </summary>
    public static DiagramDefinition Mindmap { get; } = new(
        new DiagramOrigin("freeplane", "mindmap"),
        "Mind map (radial/hierarchical, single central topic)",
        "Ideas branching from one central topic, for thinking a subject through rather than specifying it.",
        Extension: DocumentExtension);

    /// <summary>What discovery reads. One entry: this module carries one notation.</summary>
    public static DiagramDefinition[] Definitions { get; } = [Mindmap];
}
