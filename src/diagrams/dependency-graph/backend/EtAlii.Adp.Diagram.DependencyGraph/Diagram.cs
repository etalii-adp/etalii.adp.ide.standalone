using EtAlii.Adp.Documents;
using JetBrains.Annotations;
namespace EtAlii.Adp.Diagram.DependencyGraph;

/// <summary>This diagram type's identity, cataloged in docs/tools.md as `generic/dependencies`.</summary>
[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
public static class Diagram
{
    /// <summary>
    /// The Dependency Graph extension. A graph's body lives in a <c>.dgr</c> sibling of its
    /// <c>.adp</c> registration.
    /// </summary>
    /// <remarks>
    /// Owned outright, the way <c>.tml</c>, <c>.mm</c>, <c>.owm</c> and <c>.dsl</c> are owned by
    /// their types: no other tool writes a <c>.dgr</c>, so a bare one routes to this type on sight
    /// with no registration step and no Add-on-a-file path. That is why
    /// <see cref="DiagramDefinition.SharedExtension"/> stays at its default of false.
    /// </remarks>
    public const string DocumentExtension = ".dgr";

    /// <summary>Whether <paramref name="path"/> is a body this module owns.</summary>
    /// <remarks>
    /// Used where the answer is needed and no routing is at hand: a context provider is consulted
    /// for every diagram element in its scope, including elements of other types, and one that
    /// reads the document before checking is one that parses another notation's file.
    /// </remarks>
    public static bool IsBody(string path) =>
        path is { Length: > 0 } &&
        path.EndsWith(DocumentExtension, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// This module's one type, named so the module's own registrations can say which type they
    /// serve without indexing into the array. Discovery reads <see cref="Definitions"/>; the
    /// module reads this.
    /// </summary>
    public static DiagramDefinition DependencyGraph { get; } = new(
        new DiagramOrigin("generic", "dependencies"),
        "Dependency graph",
        "What needs what - named nodes on rows, joined by directed depends-on edges, placed wherever the author puts them.",
        Icon: "mdi-graph-outline",
        Extension: DocumentExtension,
        // Both axes authored and neither derived: x is a plain canvas number the author owns, y a
        // row index a drag snaps to. Nothing here is a time - that is the whole difference from
        // the timeline this module was forked from.
        Build: builder => builder.Services.AddDependencyGraph());

    /// <summary>What discovery reads. One entry: this module carries one notation.</summary>
    public static DiagramDefinition[] Definitions { get; } = [DependencyGraph];
}
