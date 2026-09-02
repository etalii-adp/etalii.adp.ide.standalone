namespace EtAlii.Adp.Diagram.WardleyMap;

/// <summary>This diagram type's identity, cataloged in docs/diagrams.md as `wardley/map`.</summary>
public static class Diagram
{
    /// <summary>
    /// The OnlineWardleyMaps file extension; the body of a Wardley map lives in an `.owm`
    /// sibling of its `.adp` registration (Requirement 2.1).
    /// </summary>
    /// <remarks>
    /// The ecosystem's own tooling also accepts `.wm` for this DSL, but a second extension
    /// cannot be declared here: <see cref="DiagramDefinition.Extension"/> is one string. A
    /// `.wm` file is opened by giving it an `.adp` registration carrying a `body:` header
    /// (Requirement 2.6), and widening the member is `azure-pipeline-diagram`'s to do.
    /// </remarks>
    public const string DocumentExtension = ".owm";

    /// <summary>
    /// Every extension this DSL is written with - the declared one, and the `.wm` the
    /// ecosystem's own tooling also accepts.
    /// </summary>
    /// <remarks>
    /// Used to answer "is this file one of mine" where the answer is needed and no routing is at
    /// hand: a context provider is consulted for EVERY diagram element in its scope, including
    /// elements of other types, and one that reads the document before checking is one that
    /// parses another notation's file. <see cref="DiagramDefinition.Extension"/> can only carry
    /// the first of these, which is why the second is named here as well.
    /// </remarks>
    public static IReadOnlyList<string> DocumentExtensions { get; } = [DocumentExtension, ".wm"];

    /// <summary>Whether <paramref name="path"/> is a body this module owns.</summary>
    public static bool IsBody(string path) =>
        path is { Length: > 0 } &&
        DocumentExtensions.Any(extension => path.EndsWith(extension, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// This module's one type, named so the module's own registrations can say which type they
    /// serve without indexing into the array. Discovery reads <see cref="Definitions"/>; the
    /// module reads this.
    /// </summary>
    public static DiagramDefinition WardleyMap { get; } = new(
        new DiagramOrigin("wardley", "map"),
        "Wardley Map",
        "A value chain positioned against evolution, so a strategy can be argued about rather than asserted.",
        Icon: "mdi-chart-line",
        Extension: DocumentExtension,
        // The map whose coordinates are the author's own claim rather than a computed layout.
        Build: builder => builder.Services.AddWardleyMap());

    /// <summary>What discovery reads. One entry: this module carries one notation.</summary>
    public static DiagramDefinition[] Definitions { get; } = [WardleyMap];
}
