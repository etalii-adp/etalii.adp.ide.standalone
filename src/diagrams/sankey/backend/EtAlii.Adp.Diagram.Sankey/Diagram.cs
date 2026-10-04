using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.Sankey;

/// <summary>This diagram type's identity, cataloged in docs/tools.md as `etalii/sankey`.</summary>
public static class Diagram
{
    /// <summary>The body of a Sankey diagram lives in a `.skv` file, ADP's own schema.</summary>
    public const string DocumentExtension = ".skv";

    /// <summary>Whether <paramref name="path"/> is a body this module owns.</summary>
    public static bool IsBody(string path) =>
        path is { Length: > 0 } &&
        path.EndsWith(DocumentExtension, StringComparison.OrdinalIgnoreCase);

    /// <summary>The Sankey diagram.</summary>
    public static DiagramDefinition Sankey { get; } = new(
        new DiagramOrigin("etalii", "sankey"),
        "Sankey diagram",
        "How a quantity divides and combines on its way from where it comes from to where it ends up: nodes as bars as tall as what passes through them, and flows as bands as thick as their value.",
        Icon: "mdi-chart-sankey",
        Extension: DocumentExtension,
        // A notation defined by this repository's user, so ADP owns both the schema and the
        // etalii/ origin. Every position is computed: a bar's height and a band's width are its value.
        Build: builder => builder.Services.AddSankey());

    /// <summary>What discovery reads. One entry: this module carries one notation.</summary>
    public static DiagramDefinition[] Definitions { get; } = [Sankey];
}
