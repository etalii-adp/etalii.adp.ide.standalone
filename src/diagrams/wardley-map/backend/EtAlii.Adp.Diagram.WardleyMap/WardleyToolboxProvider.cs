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
    public ToolOrigin Origin => Diagram.WardleyMap.Origin;

    /// <summary>The entries, in the order the palette shows them: the definition's toolbox (<see cref="WardleyDefinition.Toolbox"/>).</summary>
    public IReadOnlyList<ToolboxItemDefinition> Items => WardleyDefinition.Toolbox;
}
