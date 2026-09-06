using EtAlii.Adp.Backend.Hierarchy;
using EtAlii.Adp.Diagram;
using Google.Protobuf;

namespace EtAlii.Adp.Diagram.Rdf;

/// <summary>
/// Turns a projection into the core element and delta vocabulary. Positions come in already
/// merged - computed layout with the authored <c>layout:</c> block overlaid element by element -
/// so this class does no arithmetic of its own.
/// </summary>
public sealed class RdfElementMapper
{
    /// <summary>The mime-style kinds the canvas switches on.</summary>
    public const string ResourceType = "w3c/rdf+resource";

    /// <inheritdoc cref="ResourceType" />
    public const string EdgeType = "w3c/rdf+edge";

    /// <inheritdoc cref="ResourceType" />
    public const string TruncationType = "w3c/rdf+truncation";

    /// <summary>The truncation banner's element id - one per diagram, only when the budget cut.</summary>
    public const string TruncationId = "truncation";

    /// <summary>The drawn graph, plus the banner when the budget cut it down (Requirement 8.2).</summary>
    public IReadOnlyList<DiagramElement> Elements(
        RdfProjectionResult projection, IReadOnlyDictionary<string, RegistrationPosition> positions)
    {
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentNullException.ThrowIfNull(positions);

        var elements = new List<DiagramElement>();

        foreach (var node in projection.Nodes)
        {
            var payload = new RdfResourcePayload
            {
                Iri = node.Iri,
                Display = node.Display,
                Blank = node.Blank,
                TypeBadges = { node.Types },
            };
            foreach (var row in node.Rows)
            {
                payload.Rows.Add(new RdfLiteralRow { Predicate = row.Predicate, Value = row.Value, Annotation = row.Annotation });
            }

            elements.Add(Pack(node.Id, At(positions, node.Id), ResourceType, payload));
        }

        foreach (var edge in projection.Edges)
        {
            elements.Add(Pack(edge.Id, default, EdgeType, new RdfEdgePayload
            {
                FromElementId = edge.FromId,
                ToElementId = edge.ToId,
                Predicate = edge.Predicate,
                PredicateIri = edge.PredicateIri,
            }));
        }

        if (projection.Truncated)
        {
            elements.Add(Pack(TruncationId, At(positions, TruncationId), TruncationType, new RdfTruncationPayload
            {
                Shown = projection.Shown,
                Total = projection.Total,
            }));
        }

        return elements;
    }

    /// <summary>
    /// The difference between two renderings, as adds and removes - an edit is an add carrying
    /// the element in its new state.
    /// </summary>
    public IReadOnlyList<DiagramDelta> Diff(
        IReadOnlyList<DiagramElement> before,
        IReadOnlyList<DiagramElement> after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        var previous = before.ToDictionary(element => element.Id, StringComparer.Ordinal);
        var deltas = new List<DiagramDelta>();

        var changed = after
            .Where(element => !previous.TryGetValue(element.Id, out var was) || !Same(was, element))
            .ToArray();
        if (changed.Length > 0)
        {
            deltas.Add(new DiagramAddDelta(changed));
        }

        var current = after.Select(element => element.Id).ToHashSet(StringComparer.Ordinal);
        var gone = before.Select(element => element.Id).Where(id => !current.Contains(id)).ToArray();
        if (gone.Length > 0)
        {
            deltas.Add(new DiagramRemoveDelta(gone));
        }

        return deltas;
    }

    /// <summary>
    /// Whether two renderings of one element say the same thing. Not the record's own equality:
    /// <see cref="ReadOnlyMemory{T}"/> compares its reference rather than its bytes.
    /// </summary>
    private static bool Same(DiagramElement left, DiagramElement right) =>
        left.X.Equals(right.X)
        && left.Y.Equals(right.Y)
        && left.Type == right.Type
        && left.Payload.Span.SequenceEqual(right.Payload.Span);

    private static RegistrationPosition At(
        IReadOnlyDictionary<string, RegistrationPosition> positions, string id) =>
        positions.TryGetValue(id, out var position) ? position : default;

    private static DiagramElement Pack(string id, RegistrationPosition at, string type, IMessage payload) =>
        new(id, at.X, at.Y, type, $"type.googleapis.com/{payload.Descriptor.FullName}", payload.ToByteArray());
}
