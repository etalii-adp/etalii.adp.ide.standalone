using EtAlii.Adp.Common;
using EtAlii.Adp.Common.Wire;

namespace EtAlii.Adp.Diagram.Sparql;

/// <summary>
/// The property rows of a selected query element, contributed as data the panel renders without
/// understanding (Requirement 6.2). <b>Every row is read-only, and every row carries the reason
/// why</b> - naming the text editor as where a query is edited. The property-grid contract
/// enforces that a read-only row states a reason, so this is not decoration: it is the shape the
/// panel requires, and it is what makes the diagram honest about being a reading surface.
/// </summary>
public sealed class SparqlContextPropertyProvider : IContextPropertyProvider
{
    /// <summary>A variable's name.</summary>
    public const string VariableNameProperty = "sparql.variable.name";

    /// <summary>Whether the projection carries the variable outward.</summary>
    public const string VariableProjectedProperty = "sparql.variable.projected";

    /// <summary>How many triple-pattern positions mention the variable - its degree on the canvas.</summary>
    public const string VariableJoinCountProperty = "sparql.variable.joins";

    /// <summary>The expression that defines the variable, as written.</summary>
    public const string VariableDefinitionProperty = "sparql.variable.definition";

    /// <summary>A concrete term's display form.</summary>
    public const string TermDisplayProperty = "sparql.term.display";

    /// <summary>A concrete term's full IRI, or a literal exactly as written.</summary>
    public const string TermFullProperty = "sparql.term.full";

    /// <summary>A literal's datatype or language annotation.</summary>
    public const string TermAnnotationProperty = "sparql.term.annotation";

    /// <summary>A region's construct kind.</summary>
    public const string RegionKindProperty = "sparql.region.kind";

    /// <summary>A region's label - its graph term or service endpoint where it has one.</summary>
    public const string RegionLabelProperty = "sparql.region.label";

    /// <summary>An edge's predicate or property path, as written.</summary>
    public const string EdgeLabelProperty = "sparql.edge.label";

    /// <summary>An annotation's text, as written.</summary>
    public const string AnnotationTextProperty = "sparql.annotation.text";

    /// <summary>The query's form.</summary>
    public const string QueryFormProperty = "sparql.query.form";

    /// <summary>The query's solution modifiers and dataset clauses.</summary>
    public const string QueryModifiersProperty = "sparql.query.modifiers";

    /// <summary>A subquery's whole text, which is what a collapsed node has to offer.</summary>
    public const string SubQueryTextProperty = "sparql.subquery.text";

    private const string IdentityGroup = "Identity";
    private const string StructureGroup = "Structure";
    private const string QueryGroup = "Query";

    /// <summary>
    /// The reason every row carries. One sentence, and the same one everywhere: this diagram
    /// reads a query, and a query is edited where it was written.
    /// </summary>
    public const string ReadOnlyReason =
        "This diagram reads the query; edit the .rq file in a text editor and the diagram follows.";

    private readonly ISparqlDocumentStore _documents;

    public SparqlContextPropertyProvider(ISparqlDocumentStore documents)
    {
        ArgumentNullException.ThrowIfNull(documents);
        _documents = documents;
    }

    /// <inheritdoc />
    public ContextScope Scope => ContextScope.DiagramElement;

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<ContextPropertyDefinition>> DescribeAsync(ContextTarget target, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        cancellationToken.ThrowIfCancellationRequested();

        if (!SparqlSelection.CouldBeQueryFile(target.ResolvedFullPath))
        {
            return Rows([]);
        }

        var entry = _documents.GetOrLoad(target.ResolvedFullPath);
        if (SparqlSelection.ProjectionOf(entry) is not { } projection)
        {
            return Rows([]);
        }

        if (target.ElementId == SparqlElementMapper.HeaderId)
        {
            return Rows(
            [
                Row(QueryFormProperty, "Form", projection.HeaderForm, QueryGroup),
                Row(QueryModifiersProperty, "Modifiers", string.Join(" · ", projection.HeaderRows), QueryGroup),
            ]);
        }

        if (SparqlSelection.NodeOf(projection, target.ElementId) is { } node)
        {
            return Rows(RowsFor(node));
        }

        if (SparqlSelection.RegionOf(projection, target.ElementId) is { } region)
        {
            return Rows(
            [
                Row(RegionKindProperty, "Group", region.Kind.ToUpperInvariant(), StructureGroup),
                Row(RegionLabelProperty, "Constraint", region.Label, StructureGroup),
            ]);
        }

        if (SparqlSelection.EdgeOf(projection, target.ElementId) is { } edge)
        {
            return Rows(
            [
                Row(EdgeLabelProperty, edge.IsPath ? "Property path" : "Predicate", edge.Label, StructureGroup),
            ]);
        }

        if (SparqlSelection.AnnotationOf(projection, target.ElementId) is { } annotation)
        {
            return Rows(
            [
                Row(AnnotationTextProperty, annotation.Kind.ToUpperInvariant(), annotation.Text, StructureGroup),
            ]);
        }

        return Rows([]);
    }

    /// <summary>
    /// Refused, always: no property here is editable, so a set arriving anyway is answered with
    /// the reason rather than quietly ignored.
    /// </summary>
    public ValueTask<ContextPropertyResult> SetAsync(
        ContextTarget target,
        string propertyId,
        string value,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        cancellationToken.ThrowIfCancellationRequested();
        _ = propertyId;
        _ = value;

        return ValueTask.FromResult(ContextPropertyResult.Failure(ReadOnlyReason));
    }

    private static IReadOnlyList<ContextPropertyDefinition> RowsFor(SparqlNode node) => node.Kind switch
    {
        SparqlNodeKind.Variable or SparqlNodeKind.Anonymous =>
        [
            Row(VariableNameProperty, node.Kind == SparqlNodeKind.Anonymous ? "Anonymous variable" : "Variable", node.Display, IdentityGroup),
            Row(VariableProjectedProperty, "Projected", node.Projected ? "Yes" : "No", IdentityGroup),
            // The join count is the fact the whole diagram is built around, said in words.
            Row(VariableJoinCountProperty, "Joins", node.JoinCount.ToString(System.Globalization.CultureInfo.InvariantCulture), StructureGroup),
            Row(VariableDefinitionProperty, "Defined by", node.DefiningExpression, StructureGroup),
        ],
        SparqlNodeKind.SubSelect =>
        [
            Row(TermDisplayProperty, "Projection", node.Display, IdentityGroup),
            Row(SubQueryTextProperty, "Subquery", node.Full, QueryGroup),
        ],
        _ =>
        [
            Row(TermDisplayProperty, node.Kind == SparqlNodeKind.Iri ? "Prefixed name" : "Literal", node.Display, IdentityGroup),
            Row(TermFullProperty, node.Kind == SparqlNodeKind.Iri ? "IRI" : "As written", node.Full, IdentityGroup),
            Row(TermAnnotationProperty, "Type", node.Annotation, IdentityGroup),
        ],
    };

    /// <summary>Every row is built here, so none can be created without its reason.</summary>
    private static ContextPropertyDefinition Row(string id, string label, string value, string group) =>
        new(id, label, value, ReadOnlyReason: ReadOnlyReason, Group: group);

    private static ValueTask<IReadOnlyList<ContextPropertyDefinition>> Rows(IReadOnlyList<ContextPropertyDefinition> rows) =>
        ValueTask.FromResult(rows);
}
