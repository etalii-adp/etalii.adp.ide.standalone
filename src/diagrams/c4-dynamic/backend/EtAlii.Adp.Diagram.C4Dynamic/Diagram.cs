namespace EtAlii.Adp.Diagram.C4Dynamic;

/// <summary>This diagram type's identity, cataloged in docs/diagrams.md as `c4/dynamic`.</summary>
public static class Diagram
{
    public static DiagramDefinition[] Definitions { get; } =
    [
        new(
            new DiagramOrigin("c4", "dynamic"),
            "Dynamic (supplementary)",
            "How a handful of elements collaborate to serve one scenario, step by numbered step.",
            // All seven C4 types keep their model in a Structurizr DSL document, and several of
            // them can share one (c4-diagrams Requirement 2.2).
            ".dsl"),
    ];
}
