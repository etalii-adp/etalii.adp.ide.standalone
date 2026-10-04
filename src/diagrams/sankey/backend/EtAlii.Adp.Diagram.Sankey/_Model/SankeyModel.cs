using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.Sankey;

/// <summary>
/// Everything one <c>.skv</c> document declares: how values read, how flows are coloured, how
/// thick the bands are drawn, its nodes, its flows, and what the parser could not read.
/// </summary>
/// <remarks>
/// <b>The problems travel with the model rather than being thrown.</b> A document with one bad
/// entry still draws every good one, and the validator reports the rest without reading the text
/// again.
/// </remarks>
public sealed record SankeyModel(
    SankeySettings Settings,
    IReadOnlyList<SankeyNode> Nodes,
    IReadOnlyList<SankeyFlow> Flows,
    IReadOnlyList<SankeyProblem> Problems,
    int? Version)
{
    /// <summary>A document that declares nothing - the parser's answer to text it could not read at all.</summary>
    public static SankeyModel Empty { get; } = new(SankeySettings.Default, [], [], [], null);

    /// <summary>The version this module writes, and the only one it reads.</summary>
    public const int CurrentVersion = 1;
}

/// <summary>Something the parser could not read, and the zero-based line it was found on.</summary>
public sealed record SankeyProblem(int Line, string Message);

/// <summary>The document-wide keys, each with the value it has when the document leaves it out.</summary>
/// <param name="Format">How a value is written, <c>{value}</c> standing for the number: <c>€{value}M</c>.</param>
/// <param name="FlowColor">Whose colour a flow takes when it states none: <c>source</c> or <c>target</c>.</param>
/// <param name="Thickness">How thick every band is drawn, as a multiple of the default.</param>
/// <param name="FormatLine">The zero-based line of <c>format</c>, or -1 when the document states none.</param>
/// <param name="FlowColorLine">The line of <c>flow-color</c>, or -1.</param>
/// <param name="ThicknessLine">The line of <c>thickness</c>, or -1.</param>
public sealed record SankeySettings(
    string Format,
    string FlowColor,
    double Thickness,
    int FormatLine = -1,
    int FlowColorLine = -1,
    int ThicknessLine = -1)
{
    /// <summary>A flow takes its source's colour.</summary>
    public const string FromSource = "source";

    /// <summary>A flow takes its target's colour - the default, which is how an income statement reads.</summary>
    public const string FromTarget = "target";

    /// <summary>What a document that states nothing draws.</summary>
    public static SankeySettings Default { get; } = new(SankeyFormat.Plain, FromTarget, 1);
}

/// <summary>One node entry: something a quantity flows into and out of.</summary>
/// <param name="Id">Its id. Empty when the entry states none, which the rules report.</param>
/// <param name="Name">What its label says; its id when empty.</param>
/// <param name="Color">A palette word or <c>#rrggbb</c>, verbatim; empty for the default.</param>
/// <param name="Note">A line shown under its value.</param>
/// <param name="Format">How its value is written, overriding the document's; empty for the document's.</param>
/// <param name="Column">The one-based column it is drawn in, or <c>null</c> to have the flows decide.</param>
/// <param name="Description">Prose about it, shown in the property grid only.</param>
/// <param name="Range">The lines this entry occupies.</param>
public sealed record SankeyNode(
    string Id,
    string Name,
    string Color,
    string Note,
    string Format,
    int? Column,
    string Description,
    LineRange Range)
{
    /// <summary>What the label says: the name, or the id when the entry states no name.</summary>
    public string Label => Name.Length > 0 ? Name : Id;
}

/// <summary>One flow entry: a quantity moving from one node to another.</summary>
/// <param name="ExplicitId">The id the entry states, or empty - most flows state none.</param>
/// <param name="From">The node it leaves.</param>
/// <param name="To">The node it reaches.</param>
/// <param name="Value">How much flows, or <c>null</c> when the document states nothing.</param>
/// <param name="Step">How much one increment adds, or <c>null</c> for the default.</param>
/// <param name="Color">A palette word or <c>#rrggbb</c>; empty to take an end's colour.</param>
/// <param name="Description">Prose about the flow, shown in the property grid only.</param>
/// <param name="Range">The lines this entry occupies.</param>
public sealed record SankeyFlow(
    string ExplicitId,
    string From,
    string To,
    double? Value,
    double? Step,
    string Color,
    string Description,
    LineRange Range)
{
    /// <summary>
    /// The flow's id: the one it states, or <c>from-&gt;to</c>. A flow seldom needs a name of its
    /// own - a pair of nodes is joined once - so an author writes only its ends.
    /// </summary>
    public string Id => ExplicitId.Length > 0 ? ExplicitId : IdOf(From, To);

    /// <summary>The id a flow from <paramref name="from"/> to <paramref name="to"/> has when it states none.</summary>
    public static string IdOf(string from, string to) => $"{from}->{to}";
}

/// <summary>The sizes the document never states.</summary>
public static class SankeyGeometry
{
    /// <summary>Every node bar's width, in canvas units.</summary>
    public const double NodeWidth = 24;

    /// <summary>The distance between the left edges of two neighbouring columns.</summary>
    public const double ColumnPitch = 320;

    /// <summary>The least clear space between two nodes in one column - room for a label's three lines.</summary>
    public const double NodeGap = 36;

    /// <summary>How tall the fullest column's bars are together, at a thickness of 1.</summary>
    public const double BaseHeight = 560;

    /// <summary>How tall a node is drawn when nothing flows through it yet, so it can still be seen and picked.</summary>
    public const double MinimumNodeHeight = 6;

    /// <summary>How thin a band may be drawn, so a trickle still shows.</summary>
    public const double MinimumThickness = 1;

    /// <summary>The factor one "thicker" or "thinner" applies.</summary>
    public const double ThicknessFactor = 1.25;

    /// <summary>The thinnest and thickest a diagram may be drawn.</summary>
    public const double MinimumScale = 0.1;

    public const double MaximumScale = 10;
}
