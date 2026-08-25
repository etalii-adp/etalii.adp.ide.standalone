namespace EtAlii.Adp.Diagram.C4Container;

/// <summary>This diagram type's identity, cataloged in docs/diagrams.md as `c4/container`.</summary>
public static class Diagram
{
    public static DiagramDefinition[] Definitions { get; } =
    [
        new(
            new DiagramOrigin("c4", "container"),
            "Container",
            "The applications and data stores that make up a system, and how they talk to each other.",
            // All seven C4 types keep their model in a Structurizr DSL document, and several of
            // them can share one (c4-diagrams Requirement 2.2).
            ".dsl"),
    ];
}
