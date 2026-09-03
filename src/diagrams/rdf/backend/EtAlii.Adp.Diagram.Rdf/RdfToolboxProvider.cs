namespace EtAlii.Adp.Diagram.Rdf;

/// <summary>
/// The palette, each entry carrying only data and naming the add action its drop commits
/// (rdf-diagram Requirement 6). There is no second implementation for a drop to disagree with -
/// the drop and the menu run the same action, via placement ids.
/// </summary>
public sealed class RdfToolboxProvider(DiagramOrigin origin) : IDiagramToolboxProvider
{
    /// <inheritdoc />
    public DiagramOrigin Origin { get; } = origin;

    /// <inheritdoc />
    public IReadOnlyList<ToolboxItemDefinition> Items { get; } =
    [
        new(
            "rdf.toolbox.resource",
            "Resource",
            "mdi-card-plus-outline",
            "An IRI-named resource. Drop it where it should sit; you will be asked for its name.",
            RdfContextActionProvider.AddResourceActionId),
        new(
            "rdf.toolbox.prefix",
            "Prefix",
            "mdi-at",
            "A namespace declaration, so terms can be written short.",
            RdfContextActionProvider.AddPrefixActionId),
    ];
}
