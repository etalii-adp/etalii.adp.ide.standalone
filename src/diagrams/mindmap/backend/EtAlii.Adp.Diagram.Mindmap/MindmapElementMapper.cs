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
    /// </summary>
    public IReadOnlyList<DiagramElement> Visible(MindmapDocument document, MindmapViewState.ConnectionView view, DiagramViewport viewport)
    {
        var layout = MindmapLayout.Compute(document.Root, _metrics, view.IsFolded);
        return document.Nodes
            .Where(node => layout.ContainsKey(node.Id))
            .Where(node => Intersects(layout[node.Id], viewport))
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
