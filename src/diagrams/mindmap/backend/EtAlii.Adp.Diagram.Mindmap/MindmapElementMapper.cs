using EtAlii.Adp.Backend.Diagrams;
using Google.Protobuf;

namespace EtAlii.Adp.Diagram.Mindmap;

/// <summary>
/// Turns a mindmap - its tree, its layout, one connection's fold and viewport - into the core
/// element and delta vocabulary (Requirement 11). A node is one <see cref="DiagramElement"/>:
/// its id the node's id, its position the layout's, its type <c>freeplane/mindmap+node</c>,
/// its payload a <see cref="MindmapNodePayload"/> packed for the contract's <c>Any</c>. A
/// folded branch is a group; an edit is an add of the node's new state.
/// </summary>
public sealed class MindmapElementMapper
{
    /// <summary>The mime-style element kind a mindmap node carries on the wire (Requirement 11.2).</summary>
    public const string NodeType = "freeplane/mindmap+node";

    private readonly MindmapMetrics _metrics;

    public MindmapElementMapper(MindmapMetrics metrics)
    {
        ArgumentNullException.ThrowIfNull(metrics);
        _metrics = metrics;
    }

    /// <summary>
    /// Every node visible to a connection - inside its viewport, not under a fold - as an add.
    /// This is the baseline and the whole answer to a viewport change (Requirement 11.5).
    /// <para>
    /// "Inside" is generous on purpose, in two ways. A node the viewport merely overlaps counts
    /// as in view, because the part of it that is on screen is a node the user can see. And each
    /// in-view node brings its parent and its children with it, whether or not those fall inside
    /// the viewport: the canvas draws a connector from the boxes at its two ends, so a node whose
    /// parent was culled would lose the line running off the edge of the screen towards it.
    /// </para>
    /// <para>
    /// The second rule stops at exactly one hop. Following the partners of partners would walk
    /// the whole tree and deliver the entire map, which is what the viewport exists to avoid.
    /// </para>
    /// </summary>
    public IReadOnlyList<DiagramElement> Visible(MindmapDocument document, MindmapViewState.ConnectionView view, DiagramViewport viewport)
    {
        var layout = MindmapLayout.Compute(document.Root, _metrics, view.IsFolded);
        // Only nodes the layout placed exist at all: a folded branch's descendants have no box,
        // and must not reappear through the partner rule below.
        var placed = document.Nodes.Where(node => layout.ContainsKey(node.Id)).ToArray();

        var delivered = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in placed.Where(node => Intersects(layout[node.Id], viewport)))
        {
            delivered.Add(node.Id);
            if (node.Parent is { } parent)
            {
                delivered.Add(parent.Id);
            }

            foreach (var child in node.Children)
            {
                delivered.Add(child.Id);
            }
        }

        // Filtered back over the placed nodes rather than emitted per hop: that dedupes a node
        // reached several ways, drops any partner the layout never placed, and keeps the order
        // the document has - so the same viewport always yields the same sequence.
        return placed
            .Where(node => delivered.Contains(node.Id))
            .Select(node => ToElement(node, layout[node.Id]))
            .ToArray();
    }

    /// <summary>The layout for a document and a view - shared with the session, which needs positions for a group's parent.</summary>
    public IReadOnlyDictionary<string, MindmapBox> Layout(MindmapDocument document, MindmapViewState.ConnectionView view) =>
        MindmapLayout.Compute(document.Root, _metrics, view.IsFolded);

    /// <summary>One node as an element at a known box.</summary>
    public DiagramElement ToElement(MindmapNode node, MindmapBox box)
    {
        var payload = new MindmapNodePayload
        {
            Text = node.Text,
            Notes = node.Notes,
            HasChildren = node.HasChildren,
            Folded = false, // fold is the receiver's own view state; the element just carries content
            Link = node.Link is { } link ? new MindmapLink { Raw = link } : null,
            // Empty on the root; what the canvas draws each node's connector to.
            ParentId = node.Parent?.Id ?? "",
            // The measured box, so the canvas draws the node at its true size and anchors
            // connectors on its actual edges instead of guessing (Requirement 5.7).
            Width = box.Width,
            Height = box.Height,
        };

        return new DiagramElement(
            node.Id,
            box.CenterX,
            box.CenterY,
            NodeType,
            $"type.googleapis.com/{MindmapNodePayload.Descriptor.FullName}",
            payload.ToByteArray());
    }

    private static bool Intersects(MindmapBox box, DiagramViewport viewport) =>
        box.Right >= viewport.MinX && box.X <= viewport.MaxX && box.Bottom >= viewport.MinY && box.Y <= viewport.MaxY;
}
