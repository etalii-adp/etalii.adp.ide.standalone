using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.FunctionalDecompositionGraph;

/// <summary>This diagram type's identity, cataloged in docs/tools.md as `etalii/functional-decomposition-graph`.</summary>
/// <remarks>
/// <b>Without this class FDG never reaches the diagram catalog</b>, and nothing says so: the module was
/// deployed beside the host from task 9 onwards and discovery simply found no <c>Definitions</c> to
/// read. So the catalog test names this origin explicitly rather than trusting the generic
/// every-module comparison, which a module declaring nothing passes.
/// </remarks>
public static class Diagram
{
    /// <summary>The body of a functional decomposition graph lives in an `.fdg` file, ADP's own schema.</summary>
    public const string DocumentExtension = ".fdg";

    /// <summary>Whether <paramref name="path"/> is a body this module owns.</summary>
    public static bool IsBody(string path) =>
        path is { Length: > 0 } &&
        path.EndsWith(DocumentExtension, StringComparison.OrdinalIgnoreCase);

    /// <summary>The functional decomposition graph.</summary>
    public static DiagramDefinition FunctionalDecompositionGraph { get; } = new(
        new DiagramOrigin("etalii", "functional-decomposition-graph"),
        "Functional decomposition graph",
        "What an application is made of - its screens, the actions on them, the data it keeps and the functions behind them - each owned by one parent, with Shows for where an action leads.",
        Icon: "mdi-file-tree-outline",
        Extension: DocumentExtension,
        // A notation defined by this repository's user, so ADP owns both the schema and the
        // etalii/ origin rather than a standards body. Positions are the author's own: nothing here
        // is laid out.
        Build: builder => builder.Services.AddFunctionalDecompositionGraph());

    /// <summary>What discovery reads. One entry: this module carries one notation.</summary>
    public static DiagramDefinition[] Definitions { get; } = [FunctionalDecompositionGraph];
}
