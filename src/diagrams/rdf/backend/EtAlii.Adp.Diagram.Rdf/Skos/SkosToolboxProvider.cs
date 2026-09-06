using EtAlii.Adp.Common;
namespace EtAlii.Adp.Diagram.Rdf;

/// <summary>
/// The scheme reading's palette: a Concept and nothing else (skos-diagram Requirement 6.1) -
/// schemes and collections are modeled deliberately, not dropped, and the entry names the add
/// action its drop commits so the drop and the menu are the same edit.
/// </summary>
public sealed class SkosToolboxProvider(DiagramOrigin origin) : IDiagramToolboxProvider
{
    /// <inheritdoc />
    public DiagramOrigin Origin { get; } = origin;

    /// <inheritdoc />
    public IReadOnlyList<ToolboxItemDefinition> Items { get; } =
    [
        new(
            "skos.toolbox.concept",
            "Concept",
            "mdi-tag-plus-outline",
            "A concept, filed into the file's first scheme. Drop it where it should sit; you will be asked for its preferred label.",
            SkosActions.AddConceptActionId),
    ];
}
