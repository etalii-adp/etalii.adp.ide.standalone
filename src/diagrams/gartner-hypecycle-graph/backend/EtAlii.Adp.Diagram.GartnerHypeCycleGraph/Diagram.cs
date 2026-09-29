using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph;

/// <summary>This diagram type's identity, cataloged in docs/tools.md as `gartner/hypecycle-graph`.</summary>
/// <remarks>
/// <b>Without this class the module never reaches the diagram catalog</b>, and nothing says so:
/// discovery simply finds no <c>Definitions</c> to read. So the module's own test names this origin
/// explicitly rather than trusting the generic every-module comparison, which a module declaring
/// nothing passes.
/// </remarks>
public static class Diagram
{
    /// <summary>The body of a hype cycle graph lives in a `.ghg` file, ADP's own schema (Requirement 2.1).</summary>
    public const string DocumentExtension = ".ghg";

    /// <summary>Whether <paramref name="path"/> is a body this module owns.</summary>
    public static bool IsBody(string path) =>
        path is { Length: > 0 } &&
        path.EndsWith(DocumentExtension, StringComparison.OrdinalIgnoreCase);

    /// <summary>The Gartner hype cycle graph.</summary>
    public static DiagramDefinition HypeCycleGraph { get; } = new(
        new DiagramOrigin("gartner", "hypecycle-graph"),
        "Gartner hype cycle graph",
        "Trends placed on a shared time axis, each showing how far it has travelled through the phases of the Gartner hype cycle, with the influences trends have had on one another.",
        Icon: "mdi-chart-bell-curve-cumulative",
        Extension: DocumentExtension,
        // No established format carries trends, phases and phase-anchored influences, so ADP owns the
        // schema. The origin names the hype cycle's author, as the catalog's convention asks.
        // Positions are the author's own: nothing here is laid out.
        Build: builder => builder.Services.AddGartnerHypeCycleGraph());

    /// <summary>What discovery reads. One entry: this module carries one notation.</summary>
    public static DiagramDefinition[] Definitions { get; } = [HypeCycleGraph];
}
