namespace EtAlii.Adp.Diagram.C4Code;

/// <summary>This diagram type's identity, cataloged in docs/diagrams.md as `c4/code`.</summary>
public static class Diagram
{
    public static DiagramDefinition Definition { get; } = new(
        new DiagramOrigin("c4", "code"),
        "Code (optional)",
        // All seven C4 types keep their model in a Structurizr DSL document, and several of
        // them can share one (c4-diagrams Requirement 2.2).
        ".dsl");
}
