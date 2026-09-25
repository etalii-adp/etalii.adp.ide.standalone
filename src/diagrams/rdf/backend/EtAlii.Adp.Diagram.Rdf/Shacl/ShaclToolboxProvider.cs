using EtAlii.Adp.Documents;
namespace EtAlii.Adp.Diagram.Rdf.Shacl;

/// <summary>
/// The shapes palette, each entry carrying only data and naming the action its drop commits
/// (shacl-diagram Requirement 6.1). The drop and the menu run the same action, so there is no
/// second implementation for them to disagree about.
/// </summary>
/// <remarks>
/// The toolbox seam is origin-scoped by construction, so this palette reaches only diagrams
/// opened as <c>w3c/shacl</c> - unlike the context menu, which needed
/// <see cref="ShaclActions"/> to gate on the target's origin because a selection id is shared
/// across the family's readings.
/// </remarks>
public sealed class ShaclToolboxProvider(DiagramOrigin origin) : IDiagramToolboxProvider
{
    /// <inheritdoc />
    public DiagramOrigin Origin { get; } = origin;

    /// <inheritdoc />
    public IReadOnlyList<ToolboxItemDefinition> Items { get; } =
    [
        new(
            "shacl.toolbox.node-shape",
            "Node shape",
            "mdi-check-decagram-outline",
            "A shape that constrains focus nodes. Drop it where it should sit; you will be asked for its name.",
            ShaclActions.AddNodeShapeActionId),
        new(
            "shacl.toolbox.property-row",
            "Property row",
            "mdi-table-row-plus-after",
            "A constraint on the values reached over one path. Drop it on a shape; you will be asked for the path.",
            ShaclActions.AddPropertyRowActionId),
    ];
}
