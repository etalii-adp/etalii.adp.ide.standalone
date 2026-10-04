using EtAlii.Adp.Hierarchy;
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

    /// <summary>
    /// The elements a viewport admits: every region whose frame the rectangle touches, every
    /// node whose box it touches, and one hop outwards along the edges so a connector always
    /// has both of its ends to be drawn between.
    /// </summary>
    /// <remarks>
    /// The one-hop rule is the Ansible module's, kept rather than reinvented. A region needs no
    /// hop of its own: layout guarantees a region's bounds contain its contents, so a visible
    /// node's region is intersecting the viewport by construction.
    /// </remarks>
    public IReadOnlyList<DiagramElement> Visible(
        SparqlProjectionResult projection, SparqlLayoutResult layout, DiagramViewport viewport)
    {
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentNullException.ThrowIfNull(layout);

        var delivered = new HashSet<string>(StringComparer.Ordinal);
        foreach (var region in projection.Regions)
        {
            if (layout.RegionBounds.TryGetValue(region.Id, out var frame) && Intersects(frame, viewport))
            {
                delivered.Add(region.Id);
            }
        }

        foreach (var node in projection.Nodes)
        {
            var at = layout.NodePositions.TryGetValue(node.Id, out var position) ? position : default;
            if (Intersects(new SparqlRect(at.X, at.Y, SparqlLayout.NodeWidth, SparqlLayout.NodeHeight), viewport))
            {
                delivered.Add(node.Id);
            }
        }

        // One hop, exactly: an edge with one end in view brings the other end with it.
        foreach (var edge in projection.Edges)
        {
            if (delivered.Contains(edge.FromId) && edge.ToId.Length > 0)
            {
                delivered.Add(edge.ToId);
            }
            else if (edge.ToId.Length > 0 && delivered.Contains(edge.ToId))
            {
                delivered.Add(edge.FromId);
            }
        }

        // An edge travels when both of its ends do; one with no target travels with its source.
        var edgesById = projection.Edges.ToDictionary(edge => edge.Id, StringComparer.Ordinal);
        foreach ((string id, SparqlEdge edge) in edgesById)
        {
            if (delivered.Contains(edge.FromId) && (edge.ToId.Length == 0 || delivered.Contains(edge.ToId)))
            {
                delivered.Add(id);
            }
        }

        // Anything that is neither region, node nor edge has no position to test - the header
        // band is the case that matters - and travels always. Dropping it was the first thing
        // this filter got wrong, and two existing tests caught it immediately.
        var positioned = new HashSet<string>(StringComparer.Ordinal);
        positioned.UnionWith(projection.Regions.Select(region => region.Id));
        positioned.UnionWith(projection.Nodes.Select(node => node.Id));
        positioned.UnionWith(edgesById.Keys);

        // Rendered once and filtered, rather than re-packed here: the packing lives in one
        // place, so a payload can never differ between the whole render and the narrowed one.
        return [.. Elements(projection, layout)
            .Where(element => !positioned.Contains(element.Id) || delivered.Contains(element.Id))];
    }

    private static bool Intersects(SparqlRect box, DiagramViewport viewport) =>
        box.X <= viewport.MaxX && box.X + box.Width >= viewport.MinX &&
        box.Y <= viewport.MaxY && box.Y + box.Height >= viewport.MinY;

    private static DiagramElement Pack(string id, RegistrationPosition at, string type, IMessage payload) =>
        new(id, at.X, at.Y, type, $"type.googleapis.com/{payload.Descriptor.FullName}", payload.ToByteArray());
}
