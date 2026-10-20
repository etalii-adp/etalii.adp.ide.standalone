namespace EtAlii.Adp.Diagram.Databricks;

/// <summary>
/// The palette per diagram type, each entry carrying only data and naming the add action its
/// drop commits (databricks-diagrams Requirement 9). There is no second implementation for a
/// drop to disagree with - the drop and the menu run the same command, via placement ids.
/// </summary>
/// <remarks>
/// Derived from the toolbox of the type's bundled DISL definition (<see cref="DatabricksDefinition.Toolbox"/>), with the
/// ids of its <c>x-databricks</c> block (databricks-disl-fbl Requirement 3.1).
/// </remarks>
public sealed class DatabricksToolboxProvider(ToolOrigin origin) : IDiagramToolboxProvider
{
    /// <inheritdoc />
    public ToolOrigin Origin { get; } = origin;

    /// <inheritdoc />
    public IReadOnlyList<ToolboxItemDefinition> Items { get; } = DatabricksDefinition.Toolbox(origin);
}
