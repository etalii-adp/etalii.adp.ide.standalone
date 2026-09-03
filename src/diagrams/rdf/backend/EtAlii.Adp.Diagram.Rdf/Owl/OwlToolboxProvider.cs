namespace EtAlii.Adp.Diagram.Rdf;

/// <summary>
/// The ontology palette: Class, Object property, Datatype property and Individual, each entry
/// naming the add action its drop commits (owl-diagram Requirement 7.1). There is deliberately
/// no restriction entry: a restriction needs a structure no drop can state - the same reasoning
/// the wardley-map spec recorded for links, applied to expressions.
/// </summary>
public sealed class OwlToolboxProvider(DiagramOrigin origin) : IDiagramToolboxProvider
{
    /// <inheritdoc />
    public DiagramOrigin Origin { get; } = origin;

    /// <inheritdoc />
    public IReadOnlyList<ToolboxItemDefinition> Items { get; } =
    [
        new(
            "owl.toolbox.class",
            "Class",
            "mdi-shape-circle-plus",
            "A named class. Drop it where it should sit; you will be asked for its name.",
            RdfContextActionProvider.AddClassActionId),
        new(
            "owl.toolbox.object-property",
            "Object property",
            "mdi-ray-start-arrow",
            "A property relating individuals to individuals - drawn as an edge once it has a domain and a range.",
            RdfContextActionProvider.AddObjectPropertyActionId),
        new(
            "owl.toolbox.datatype-property",
            "Datatype property",
            "mdi-form-textbox",
            "A property relating individuals to data values - drawn as an edge to its datatype.",
            RdfContextActionProvider.AddDatatypePropertyActionId),
        new(
            "owl.toolbox.individual",
            "Individual",
            "mdi-account-outline",
            "A named individual - a member of the classes this ontology describes.",
            RdfContextActionProvider.AddIndividualActionId),
    ];
}
