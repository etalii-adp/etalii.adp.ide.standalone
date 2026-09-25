using EtAlii.Adp.Documents;
namespace EtAlii.Adp.Diagram.C4;

/// <summary>
/// The seven C4 notations, cataloged in docs/diagrams.md as `c4/context`, `c4/container`,
/// `c4/component`, `c4/code`, `c4/system-landscape`, `c4/dynamic` and `c4/deployment`.
/// </summary>
/// <remarks>
/// One class for all seven, because one engine serves all seven: they share a model, a parser,
/// a writer and a layout, and differ only in which view of that model they draw. They were
/// seven assemblies for one reason - discovery read a singular <c>Definition</c> property, so
/// seven types meant seven classes and seven projects to hold them. Discovery now reads a
/// <c>Definitions</c> array and the reason is gone.
/// </remarks>
public static class Diagram
{
    /// <summary>
    /// The Structurizr DSL extension. All seven types keep their model in a `.dsl` document and
    /// several of them can share one, which is the whole point of the family
    /// (c4-diagrams Requirement 2.2).
    /// </summary>
    public const string DocumentExtension = ".dsl";

    /// <summary>The map to start with: the system in its world.</summary>
    public static DiagramDefinition SystemContext { get; } = new(
        new DiagramOrigin("c4", "context"),
        "System Context",
        "The system in its world: who uses it, and what it depends on. The map to start with.",
        Icon: "mdi-earth",
        Extension: DocumentExtension,
        // Seven C4 types over one shared engine, differing only in the view each binds.
        // We register these services only once.
        Build: builder => builder.Services.AddC4());

    /// <summary>Inside the system: its applications and data stores.</summary>
    public static DiagramDefinition Container { get; } = new(
        new DiagramOrigin("c4", "container"),
        "Container",
        "The applications and data stores that make up a system, and how they talk to each other.",
        Icon: "mdi-package-variant",
        Extension: DocumentExtension);

    /// <summary>Inside one container: its components.</summary>
    public static DiagramDefinition Component { get; } = new(
        new DiagramOrigin("c4", "component"),
        "Component",
        "The components inside one container, and how each collaborates with the others.",
        Icon: "mdi-puzzle-outline",
        Extension: DocumentExtension);

    /// <summary>
    /// Inside one component. C4 marks this level optional and discourages drawing it by hand,
    /// and ADP has no canvas for it - it claims no extension, so it never competes for a bare
    /// `.dsl` file the way the other six do (c4-diagrams Requirement 11).
    /// </summary>
    public static DiagramDefinition Code { get; } = new(
        new DiagramOrigin("c4", "code"),
        "Code (optional)",
        "The classes and their relations inside one component, at the level source code is written.",
        Icon: "mdi-code-braces");

    /// <summary>Above any single system: the enterprise around them.</summary>
    public static DiagramDefinition SystemLandscape { get; } = new(
        new DiagramOrigin("c4", "system-landscape"),
        "System Landscape (supplementary)",
        "Every system in an enterprise and how they relate, above any single one of them.",
        Icon: "mdi-terrain",
        Extension: DocumentExtension);

    /// <summary>One scenario, step by numbered step.</summary>
    public static DiagramDefinition Dynamic { get; } = new(
        new DiagramOrigin("c4", "dynamic"),
        "Dynamic (supplementary)",
        "How a handful of elements collaborate to serve one scenario, step by numbered step.",
        Icon: "mdi-motion-play-outline",
        Extension: DocumentExtension);

    /// <summary>The static model mapped onto the infrastructure it runs on.</summary>
    public static DiagramDefinition Deployment { get; } = new(
        new DiagramOrigin("c4", "deployment"),
        "Deployment (supplementary)",
        "Which containers run on which infrastructure, in one environment.",
        Icon: "mdi-server-network",
        Extension: DocumentExtension);

    /// <summary>
    /// What discovery reads, in the order C4 itself introduces the levels: the four static
    /// levels from the outside in, then the three supplementary views.
    /// </summary>
    public static DiagramDefinition[] Definitions { get; } =
    [
        SystemContext,
        Container,
        Component,
        Code,
        SystemLandscape,
        Dynamic,
        Deployment,
    ];
}
