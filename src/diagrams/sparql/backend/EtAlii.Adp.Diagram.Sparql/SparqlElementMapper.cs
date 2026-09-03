using EtAlii.Adp.Backend.Diagrams;
using EtAlii.Adp.Backend.Hierarchy;
using Google.Protobuf;

namespace EtAlii.Adp.Diagram.Sparql;

/// <summary>
/// Turns a projection and its computed geometry into the core element and delta vocabulary.
/// Positions arrive already merged - computed layout with the authored <c>layout:</c> block
/// overlaid - so this class does no arithmetic of its own.
/// </summary>
public sealed class SparqlElementMapper
{
    /// <summary>The mime-style kinds the canvas switches on.</summary>
    public const string VariableType = "w3c/sparql+variable";

    /// <inheritdoc cref="VariableType" />
    public const string TermType = "w3c/sparql+term";

    /// <inheritdoc cref="VariableType" />
    public const string EdgeType = "w3c/sparql+edge";

    /// <inheritdoc cref="VariableType" />
    public const string RegionType = "w3c/sparql+region";

    /// <inheritdoc cref="VariableType" />
    public const string AnnotationType = "w3c/sparql+annotation";

    /// <inheritdoc cref="VariableType" />
    public const string HeaderType = "w3c/sparql+header";

    /// <inheritdoc cref="VariableType" />
    public const string TruncationType = "w3c/sparql+truncation";

    /// <summary>The header band's element id - one per diagram, always present.</summary>
    public const string HeaderId = "header:query";

    /// <summary>The truncation banner's element id - only when the sanity bound cut.</summary>
    public const string TruncationId = "truncation";

    /// <summary>
    /// The drawn query: regions first so a frame is behind its contents, then nodes, edges,
    /// annotations, the header band, and the banner when the bound cut (Requirement 7.5).
    /// </summary>
    public IReadOnlyList<DiagramElement> Elements(SparqlProjectionResult projection, SparqlLayoutResult layout)
    {
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentNullException.ThrowIfNull(layout);

        var elements = new List<DiagramElement>();

        foreach (var region in projection.Regions)
        {
            var frame = layout.RegionBounds.TryGetValue(region.Id, out var bounds) ? bounds : default;
            elements.Add(Pack(region.Id, new RegistrationPosition(frame.X, frame.Y), RegionType, new SparqlRegionPayload
            {
                Kind = region.Kind,
                Label = region.Label,
                ParentRegionId = region.ParentRegionId,
                Width = frame.Width,
                Height = frame.Height,
            }));
        }

        foreach (var node in projection.Nodes)
        {
            var at = layout.NodePositions.TryGetValue(node.Id, out var position) ? position : default;
            switch (node.Kind)
            {
                case SparqlNodeKind.Variable or SparqlNodeKind.Anonymous:
                    elements.Add(Pack(node.Id, at, VariableType, new SparqlVariablePayload
                    {
                        Name = node.Display,
                        Projected = node.Projected,
                        JoinCount = node.JoinCount,
                        DefiningExpression = node.DefiningExpression,
                        Anonymous = node.Kind == SparqlNodeKind.Anonymous,
                    }));
                    break;

                case SparqlNodeKind.SubSelect:
                    // A collapsed subquery travels as a term whose display is its projection and
                    // whose full text the property grid shows (Requirement 3.6).
                    elements.Add(Pack(node.Id, at, TermType, new SparqlTermPayload
                    {
                        Display = node.Display,
                        Full = node.Full,
                        Annotation = "subquery",
                        Kind = SparqlTermKind.Unspecified,
                    }));
                    break;

                default:
                    elements.Add(Pack(node.Id, at, TermType, new SparqlTermPayload
                    {
                        Display = node.Display,
                        Full = node.Full,
                        Annotation = node.Annotation,
                        Kind = node.Kind == SparqlNodeKind.Iri ? SparqlTermKind.Iri : SparqlTermKind.Literal,
                    }));
                    break;
            }
        }

        foreach (var edge in projection.Edges)
        {
            elements.Add(Pack(edge.Id, default, EdgeType, new SparqlEdgePayload
            {
                FromElementId = edge.FromId,
                ToElementId = edge.ToId,
                Label = edge.Label,
                IsPath = edge.IsPath,
            }));
        }

        foreach (var annotation in projection.Annotations)
        {
            elements.Add(Pack(annotation.Id, default, AnnotationType, new SparqlAnnotationPayload
            {
                Kind = annotation.Kind,
                Text = annotation.Text,
                AttachedTo = annotation.AttachedToId,
            }));
        }

        elements.Add(Pack(HeaderId, default, HeaderType, new SparqlHeaderPayload
        {
            Form = projection.HeaderForm,
            ModifierRows = { projection.HeaderRows },
        }));

        if (projection.Truncated)
        {
            elements.Add(Pack(TruncationId, default, TruncationType, new SparqlTruncationPayload
            {
                Shown = projection.Shown,
                Total = projection.Total,
            }));
        }

        return elements;
    }

    /// <summary>The difference between two renderings, as adds and removes - a change is an add carrying the element in its new state.</summary>
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

    /// <summary>Whether two renderings of one element say the same thing - not the record's own equality, which compares payload memory by reference.</summary>
    private static bool Same(DiagramElement left, DiagramElement right) =>
        left.X.Equals(right.X)
        && left.Y.Equals(right.Y)
        && left.Type == right.Type
        && left.Payload.Span.SequenceEqual(right.Payload.Span);

    private static DiagramElement Pack(string id, RegistrationPosition at, string type, IMessage payload) =>
        new(id, at.X, at.Y, type, $"type.googleapis.com/{payload.Descriptor.FullName}", payload.ToByteArray());
}
