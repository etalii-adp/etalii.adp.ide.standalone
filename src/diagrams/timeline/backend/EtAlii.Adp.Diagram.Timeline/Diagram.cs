using EtAlii.Adp.Common;
namespace EtAlii.Adp.Diagram.Timeline;

/// <summary>This diagram type's identity, cataloged in docs/diagrams.md as `generic/timeline`.</summary>
public static class Diagram
{
    /// <summary>
    /// The Timeline Markup Language extension. A timeline's body lives in a <c>.tml</c> sibling
    /// of its <c>.adp</c> registration (Requirement 1.1).
    /// </summary>
    /// <remarks>
    /// Owned outright, the way <c>.mm</c>, <c>.owm</c> and <c>.dsl</c> are owned by their types:
    /// no other tool writes a <c>.tml</c>, so a bare one routes to this type on sight with no
    /// registration step and no Add-on-a-file path (Requirement 1.2). That is why
    /// <see cref="DiagramDefinition.SharedExtension"/> stays at its default of false.
    /// </remarks>
    public const string DocumentExtension = ".tml";

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
    public static DiagramDefinition Timeline { get; } = new(
        new DiagramOrigin("generic", "timeline"),
        "Timeline",
        "When things happen and for how long - periods and moments on rows along a time axis, connected however the author means it.",
        Icon: "mdi-chart-timeline",
        Extension: DocumentExtension,
        // Both axes authored, neither a raw coordinate: x is authored as time through begin/end,
        // y as a row index a drag snaps to.
        Build: builder => builder.Services.AddTimeline());

    /// <summary>What discovery reads. One entry: this module carries one notation.</summary>
    public static DiagramDefinition[] Definitions { get; } = [Timeline];
}
