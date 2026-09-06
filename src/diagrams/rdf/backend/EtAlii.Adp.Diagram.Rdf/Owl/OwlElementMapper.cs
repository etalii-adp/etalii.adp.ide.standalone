using EtAlii.Adp.Backend.Hierarchy;
using EtAlii.Adp.Diagram;
using Google.Protobuf;

namespace EtAlii.Adp.Diagram.Rdf;

/// <summary>
/// Turns the ontology reading's graph into the core element vocabulary. Positions come in
/// already merged - <see cref="OwlLayout.Apply"/> has enforced the boundary, so an expression
/// node's position here is always its computed one. Diffing is the family mapper's, unchanged.
/// </summary>
public sealed class OwlElementMapper
{
    /// <summary>The mime-style kinds the canvas switches on.</summary>
    public const string NodeType = "w3c/owl+node";

    /// <inheritdoc cref="NodeType" />
    public const string EdgeType = "w3c/owl+edge";

    /// <inheritdoc cref="NodeType" />
    public const string ExpressionType = "w3c/owl+expression";

    /// <inheritdoc cref="NodeType" />
    public const string TruncationType = "w3c/owl+truncation";

    /// <summary>The truncation banner's element id - one per diagram, only when the budget cut.</summary>
    public const string TruncationId = "truncation";

    /// <summary>The drawn ontology, plus the banner when the budget cut it down (Requirement 8.3).</summary>
    public IReadOnlyList<DiagramElement> Elements(
        OwlGraphResult graph, IReadOnlyDictionary<string, RegistrationPosition> positions)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(positions);

        var elements = new List<DiagramElement>();

        foreach (var node in graph.Nodes)
        {
            if (node.Kind is OwlNodeKind.Restriction or OwlNodeKind.Operator)
            {
                elements.Add(Pack(node.Id, At(positions, node.Id), ExpressionType, new OwlExpressionPayload
                {
                    Label = node.Display,
                    Elided = node.Elided,
                    Malformed = node.Malformed,
                    OwnerElementId = node.OwnerId,
                    Kind = node.Kind == OwlNodeKind.Operator ? "operator" : "restriction",
                }));
                continue;
            }

            var payload = new OwlNodePayload
            {
                Kind = KindOf(node.Kind),
                Iri = node.Iri,
                Display = node.Display,
                Deprecated = node.Deprecated,
                External = node.External,
                Badges = { node.Badges },
            };
            foreach (var row in node.Rows)
            {
                payload.Rows.Add(new RdfLiteralRow { Predicate = row.Predicate, Value = row.Value, Annotation = row.Annotation });
            }

            elements.Add(Pack(node.Id, At(positions, node.Id), NodeType, payload));
        }

        foreach (var edge in graph.Edges)
        {
            elements.Add(Pack(edge.Id, default, EdgeType, new OwlEdgePayload
            {
                Kind = KindOf(edge.Kind),
                FromElementId = edge.FromId,
                ToElementId = edge.ToId,
                Label = edge.Label,
                PropertyIri = edge.PropertyIri,
            }));
        }

        if (graph.Truncated)
        {
            elements.Add(Pack(TruncationId, At(positions, TruncationId), TruncationType, new RdfTruncationPayload
            {
                Shown = graph.Shown,
                Total = graph.Total,
            }));
        }

        return elements;
    }

    private static string KindOf(OwlNodeKind kind) => kind switch
    {
        OwlNodeKind.Class => "class",
        OwlNodeKind.Datatype => "datatype",
        OwlNodeKind.Individual => "individual",
        OwlNodeKind.Thing => "thing",
        OwlNodeKind.OntologyHeader => "ontology",
        _ => "class",
    };

    private static string KindOf(OwlEdgeKind kind) => kind switch
    {
        OwlEdgeKind.Subclass => "subclass",
        OwlEdgeKind.Equivalent => "equivalent",
        OwlEdgeKind.Disjoint => "disjoint",
        OwlEdgeKind.ObjectProperty => "object-property",
        OwlEdgeKind.DatatypeProperty => "datatype-property",
        OwlEdgeKind.Assertion => "assertion",
        OwlEdgeKind.Expression => "expression",
        _ => "subclass",
    };

    private static RegistrationPosition At(
        IReadOnlyDictionary<string, RegistrationPosition> positions, string id) =>
        positions.TryGetValue(id, out var position) ? position : default;

    private static DiagramElement Pack(string id, RegistrationPosition at, string type, IMessage payload) =>
        new(id, at.X, at.Y, type, $"type.googleapis.com/{payload.Descriptor.FullName}", payload.ToByteArray());
}
