namespace EtAlii.Adp.Diagram.WardleyMap;

/// <summary>
/// What a Wardley map offers in the Toolbox: the statements a user writes rather than derives
/// (Requirement 13). Described by the backend as data - the client renders a palette it does not
/// interpret, exactly as it renders context actions.
/// </summary>
/// <remarks>
/// <para>
/// <b>Market and Ecosystem are components, not kinds.</b> The DSL has three statement kinds -
/// `component`, `anchor`, `submap` - and five decorators. A market IS a component carrying
/// `(market)`, so its entry drops exactly that (Requirement 13.2). Offering it as a kind of its
/// own would be a palette that creates something the file cannot express.
/// </para>
/// <para>
/// <b>There is deliberately no Link entry.</b> A link needs two endpoints and a drop has one, so
/// dropping a link would always leave half of it unsaid. It is created through the context
/// action instead - `wardley.link` for a dependency, `wardley.flow` for a flow - which is what
/// Requirement 13.6 asks a type in this position to say out loud.
/// </para>
/// <para>
/// <b>Pipeline drops onto a component</b>, because that is what a pipeline belongs to: its
/// entry reuses the same action the menu's "Start a pipeline…" uses, which asks for the first
/// component to put in it.
/// </para>
/// <para>
/// The Toolbox panel renders this now, and the canvas registers it while a map is open. The
/// provider stays as Requirement 13.7 asked - <see cref="Items"/> static and assertable
/// without a UI - and the two criteria that need contract fields that do not exist (13.4's
/// drop position, 13.5's unavailable entries) remain reported as gaps rather than implemented
/// against a contract that has no field for either.
/// </para>
/// </remarks>
public sealed class WardleyToolboxProvider : IDiagramToolboxProvider
{
    public DiagramOrigin Origin => Diagram.WardleyMap.Origin;

    /// <summary>
    /// The entries, in the order the palette shows them: the two that make a value chain first,
    /// then the two decorated components, then the structures, then the two kinds of writing.
    /// </summary>
    public IReadOnlyList<ToolboxItemDefinition> Items { get; } =
    [
        new(
            "wardley.toolbox.component",
            "Component",
            "mdi-shape-outline",
            "Something the value chain is made of. Where you put it is what you are claiming about it.",
            WardleyContextActionProvider.AddComponentActionId),
        new(
            "wardley.toolbox.anchor",
            "Anchor",
            "mdi-anchor",
            "The user need the whole chain hangs from. A map usually has one.",
            WardleyContextActionProvider.AddAnchorActionId),
        new(
            "wardley.toolbox.market",
            "Market",
            "mdi-store-outline",
            "A component supplied by a market rather than by one supplier.",
            WardleyContextActionProvider.AddMarketActionId),
        new(
            "wardley.toolbox.ecosystem",
            "Ecosystem",
            "mdi-graph-outline",
            "A component with an ecosystem of others built around it.",
            WardleyContextActionProvider.AddEcosystemActionId),
        new(
            "wardley.toolbox.submap",
            "Submap",
            "mdi-map-outline",
            "A reference to another map, which activating opens.",
            WardleyContextActionProvider.AddSubmapActionId),
        new(
            "wardley.toolbox.pipeline",
            "Pipeline",
            "mdi-pipe",
            "A set of choices behind one component. Drop it on the component it belongs to.",
            WardleyContextActionProvider.AddToPipelineActionId),
        new(
            "wardley.toolbox.note",
            "Note",
            "mdi-note-outline",
            "Free text pinned to a place on the map.",
            WardleyContextActionProvider.AddNoteActionId),
        new(
            "wardley.toolbox.annotation",
            "Annotation",
            "mdi-comment-text-outline",
            "A numbered remark, shown on the map and listed in its own block.",
            WardleyContextActionProvider.AddAnnotationActionId),
    ];
}
