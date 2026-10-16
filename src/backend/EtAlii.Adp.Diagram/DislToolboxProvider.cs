using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram;

/// <summary>
/// The palette of a diagram type with a bundled DISL definition: the definition's toolbox, each entry
/// carrying only data and naming the action its drop commits. A module registers one of these rather
/// than a class of its own.
/// </summary>
/// <param name="origin">The diagram type whose toolbox this describes.</param>
/// <param name="definition">The type's bundled definition.</param>
public sealed class DislToolboxProvider(DiagramOrigin origin, DislDiagramDefinition definition) : IDiagramToolboxProvider
{
    /// <inheritdoc />
    public DiagramOrigin Origin { get; } = origin;

    /// <inheritdoc />
    public IReadOnlyList<ToolboxItemDefinition> Items => definition.Toolbox;
}
