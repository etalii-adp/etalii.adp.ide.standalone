using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.SupplyChain;

/// <summary>
/// Everything one <c>.supply</c> document declares: its groups, its nodes, its flows, and what the
/// parser could not read.
/// </summary>
/// <remarks>
/// <b>The problems travel with the model rather than being thrown.</b> A document with one bad entry
/// still draws every good one, and the validator reports the rest without reading the text again.
/// </remarks>
public sealed record SupplyChainModel(
    IReadOnlyList<SupplyChainGroup> Groups,
    IReadOnlyList<SupplyChainNode> Nodes,
    IReadOnlyList<SupplyChainFlow> Flows,
    IReadOnlyList<SupplyChainProblem> Problems,
    int? Version)
{
    /// <summary>A document that declares nothing - the parser's answer to text it could not read at all.</summary>
    public static SupplyChainModel Empty { get; } = new([], [], [], [], null);

    /// <summary>The version this module writes, and the only one it reads.</summary>
    public const int CurrentVersion = 1;
}

/// <summary>Something the parser could not read, and the zero-based line it was found on.</summary>
public sealed record SupplyChainProblem(int Line, string Message);

/// <summary>One group entry: a region, a company or a tier that nodes are drawn inside.</summary>
/// <param name="Id">Its id. Empty when the entry states none, which the rules report.</param>
/// <param name="Name">What the frame's title says.</param>
/// <param name="Description">Prose about the group, shown in the property grid only.</param>
/// <param name="Range">The lines this entry occupies.</param>
/// <param name="X">The authored LEFT edge of the frame while the group has no members, or <c>null</c>.</param>
/// <param name="Y">The authored TOP edge of the frame while the group has no members, or <c>null</c>.</param>
public sealed record SupplyChainGroup(string Id, string Name, string Description, LineRange Range, double? X = null, double? Y = null)
{
    /// <summary>Whether the document places this group's frame for when it has no members.</summary>
    public bool IsPlaced => X is not null && Y is not null;
}

/// <summary>One node entry: a stage of the chain.</summary>
/// <param name="Id">Its id. Empty when the entry states none, which the rules report.</param>
/// <param name="Type">Its stage, with a legacy word read as the stage it became. An unknown value survives verbatim and is reported.</param>
/// <param name="Name">Its name.</param>
/// <param name="Description">Prose about it, shown in the property grid only.</param>
/// <param name="Group">The id of the group it is drawn inside, or empty.</param>
/// <param name="Quantity">Its value - a capacity, a stock, a demand - or <c>null</c> when it states none.</param>
/// <param name="Unit">What the quantity counts.</param>
/// <param name="Step">How much one increment adds, or <c>null</c> for the default.</param>
/// <param name="X">The authored LEFT edge, or <c>null</c> when the layout places it.</param>
/// <param name="Y">The authored TOP edge, or <c>null</c> when the layout places it.</param>
/// <param name="Range">The lines this entry occupies.</param>
public sealed record SupplyChainNode(
    string Id,
    string Type,
    string Name,
    string Description,
    string Group,
    double? Quantity,
    string Unit,
    double? Step,
    double? X,
    double? Y,
    LineRange Range)
{
    /// <summary>Whether the document places this node itself, rather than leaving it to the layout.</summary>
    public bool IsPlaced => X is not null && Y is not null;
}

/// <summary>One flow entry: goods moving from a supplying node to a consuming one.</summary>
/// <param name="Id">Its id. Empty when the entry states none, which the rules report.</param>
/// <param name="From">The supplying node's id.</param>
/// <param name="To">The consuming node's id.</param>
/// <param name="Product">What flows, which is the flow's label.</param>
/// <param name="Description">Prose about the flow, shown in the property grid only.</param>
/// <param name="Volume">How much flows, or <c>null</c> when the document states nothing.</param>
/// <param name="Unit">What the volume counts.</param>
/// <param name="Step">How much one increment adds, or <c>null</c> for the default.</param>
/// <param name="Range">The lines this entry occupies.</param>
public sealed record SupplyChainFlow(
    string Id,
    string From,
    string To,
    string Product,
    string Description,
    double? Volume,
    string Unit,
    double? Step,
    LineRange Range);

/// <summary>
/// The stages a node may declare, stated once, with the word the canvas shows for each.
/// </summary>
/// <remarks>
/// <para>
/// Strings rather than an enum, because <b>the parser must keep a stage it does not recognise</b>:
/// the entry survives the round trip, draws nothing, and is reported.
/// </para>
/// <para>
/// <b>The words are roles, not industries</b>, so one notation reads for goods, energy, data or
/// services alike. The first words this module wrote - <c>raw-material</c>, <c>supplier</c>,
/// <c>manufacturer</c>, <c>assembler</c>, <c>distributor</c>, <c>retailer</c> - still read, as the
/// stage each became; a line nobody edits keeps its old word.
/// </para>
/// </remarks>
public static class SupplyChainNodeTypes
{
    public const string Source = "source";
    public const string Processor = "processor";
    public const string Producer = "producer";
    public const string Integrator = "integrator";
    public const string Hub = "hub";
    public const string Outlet = "outlet";
    public const string Consumer = "consumer";

    /// <summary>The seven, in the order goods pass through them.</summary>
    public static readonly IReadOnlyList<string> All = [Source, Processor, Producer, Integrator, Hub, Outlet, Consumer];

    /// <summary>The words this module wrote before the stages were named as roles, and the stage each reads as.</summary>
    private static readonly IReadOnlyDictionary<string, string> Legacy = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["raw-material"] = Source,
        ["supplier"] = Processor,
        ["manufacturer"] = Producer,
        ["assembler"] = Integrator,
        ["distributor"] = Hub,
        ["retailer"] = Outlet,
    };

    /// <summary>The stage a document's word means: a legacy word reads as its successor, anything else as itself.</summary>
    public static string Normalize(string type) => Legacy.TryGetValue(type, out var current) ? current : type;

    /// <summary>Where a stage comes in the chain: 0 for a source, 6 for a consumer.</summary>
    public static int RankOf(string type) => Math.Max(0, All.ToList().IndexOf(Normalize(type)));

    /// <summary>Whether the text names one of the seven, or a legacy word for one.</summary>
    public static bool IsKnown(string type) => All.Contains(Normalize(type), StringComparer.Ordinal);

    /// <summary>The stage as a reader names it: <c>source</c> reads "Source".</summary>
    public static string Display(string type) => Normalize(type) switch
    {
        Source => "Source",
        Processor => "Processor",
        Producer => "Producer",
        Integrator => "Integrator",
        Hub => "Hub",
        Outlet => "Outlet",
        Consumer => "Consumer",
        _ => type,
    };
}

/// <summary>The sizes the document never states.</summary>
public static class SupplyChainGeometry
{
    /// <summary>Every node's width, in canvas units.</summary>
    public const double NodeWidth = 192;

    /// <summary>Every node's height, in canvas units.</summary>
    public const double NodeHeight = 96;

    /// <summary>The space between a group's frame and its members, on the left, right and bottom.</summary>
    public const double GroupPadding = 20;

    /// <summary>The space above a group's members, where its title sits.</summary>
    public const double GroupHeader = 40;

    /// <summary>The width of an empty group's frame: exactly the frame around one node.</summary>
    public const double EmptyGroupWidth = NodeWidth + (2 * GroupPadding);

    /// <summary>The height of an empty group's frame: exactly the frame around one node.</summary>
    public const double EmptyGroupHeight = GroupHeader + NodeHeight + GroupPadding;

    /// <summary>What one increment adds when a node or a flow states no step.</summary>
    public const double DefaultStep = 1;
}
