using EtAlii.Adp.Layout;

namespace EtAlii.Adp.Diagram.Mindmap;

/// <summary>
/// The conventional mindmap arrangement, computed from the tree alone: the root centred at the
/// origin, its branches to either side, each node's children stacked beside it with depth
/// expressed as distance from the root (Requirement 5.1). A pure function with no gRPC,
/// filesystem or canvas dependency, so it is tested on positions and nothing else.
/// </summary>
/// <remarks>
/// Deterministic by construction - the same tree and the same fold set always give the same
/// boxes, so two clients see one map and a reconnect does not reshuffle it (Requirement 5.2).
/// Folded branches are skipped entirely: a hidden node occupies no space, which is what lets
/// folding make room.
/// <para>
/// Which side a first-level branch goes on follows Freeplane's <c>POSITION</c> attribute
/// when the file has one, and alternates right, left, right, ... when it does not - the
/// rule Freeplane itself applies to an unpositioned map.
/// </para>
/// </remarks>
public static class MindmapLayout
{
    /// <summary>
    /// Boxes for every visible node, keyed by node id. <paramref name="isFolded"/> says which
    /// nodes hide their children on the connection being laid out. <paramref name="textOf"/> is the text a node shows, which a
    /// linked node takes from its target (DISL 4.12); its own text when null.
    /// </summary>
    public static IReadOnlyDictionary<string, Rect> Compute(MindmapNode root, MindmapMetrics metrics, Func<MindmapNode, bool> isFolded, Func<MindmapNode, string>? textOf = null)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(isFolded);
        textOf ??= OwnText;

        var boxes = new Dictionary<string, Rect>(StringComparer.Ordinal);
        var rootSize = metrics.Measure(textOf(root));
        var rootBox = new Rect(-rootSize.Width / 2, -rootSize.Height / 2, rootSize.Width, rootSize.Height);
        boxes[root.Id] = rootBox;

        if (isFolded(root))
        {
            return boxes;
        }

        // First-level branches split into the two sides, each side laid out as its own column
        // of subtrees centred on the root.
        var right = new List<MindmapNode>();
        var left = new List<MindmapNode>();
        var alternate = 0;
        foreach (var child in root.Children)
        {
            // Freeplane 1.12 renamed the values: what 1.11 saved as "left"/"right" a genuine
            // 1.12 save writes as "top_or_left"/"bottom_or_right" (see Fixtures/readme.md).
            // Both vocabularies are honoured; anything else alternates like an unpositioned map.
            var side = child.Position?.ToLowerInvariant();
            var goesLeft = side is "left" or "top_or_left"
                || (side is not ("right" or "bottom_or_right") && alternate++ % 2 == 1);
            (goesLeft ? left : right).Add(child);
        }

        PlaceColumn(right, rootBox.Right + metrics.GapBeside(rootBox.Width), rootBox.CenterY, towardsRight: true, metrics, isFolded, textOf, boxes);
        PlaceColumn(left, rootBox.X - metrics.GapBeside(rootBox.Width), rootBox.CenterY, towardsRight: false, metrics, isFolded, textOf, boxes);

        return boxes;
    }

    /// <summary>Lays a list of sibling subtrees out as a vertical column centred on <paramref name="centerY"/>.</summary>
    private static void PlaceColumn(
        IReadOnlyList<MindmapNode> siblings,
        double edgeX,
        double centerY,
        bool towardsRight,
        MindmapMetrics metrics,
        Func<MindmapNode, bool> isFolded,
        Func<MindmapNode, string> textOf,
        Dictionary<string, Rect> boxes)
    {
        if (siblings.Count == 0)
        {
            return;
        }

        var heights = siblings.Select(sibling => SubtreeHeight(sibling, metrics, isFolded, textOf)).ToArray();
        var widths = siblings.Select(sibling => metrics.Measure(textOf(sibling)).Width).ToArray();
        var total = heights.Sum();
        for (var i = 0; i < siblings.Count - 1; i++)
        {
            total += metrics.GapBetween(widths[i], widths[i + 1]);
        }

        var y = centerY - total / 2;

        for (var i = 0; i < siblings.Count; i++)
        {
            var slotCenterY = y + heights[i] / 2;
            PlaceSubtree(siblings[i], edgeX, slotCenterY, towardsRight, metrics, isFolded, textOf, boxes);
            y += heights[i];
            if (i < siblings.Count - 1)
            {
                y += metrics.GapBetween(widths[i], widths[i + 1]);
            }
        }
    }

    /// <summary>Places one node at <paramref name="edgeX"/> vertically centred on <paramref name="centerY"/>, then its children beyond it.</summary>
    private static void PlaceSubtree(
        MindmapNode node,
        double edgeX,
        double centerY,
        bool towardsRight,
        MindmapMetrics metrics,
        Func<MindmapNode, bool> isFolded,
        Func<MindmapNode, string> textOf,
        Dictionary<string, Rect> boxes)
    {
        var size = metrics.Measure(textOf(node));
        var x = towardsRight ? edgeX : edgeX - size.Width;
        var box = new Rect(Math.Round(x, 2), Math.Round(centerY - size.Height / 2, 2), size.Width, size.Height);
        boxes[node.Id] = box;

        if (isFolded(node))
        {
            return;
        }

        var childEdge = towardsRight ? box.Right + metrics.GapBeside(box.Width) : box.X - metrics.GapBeside(box.Width);
        PlaceColumn(node.Children, childEdge, centerY, towardsRight, metrics, isFolded, textOf, boxes);
    }

    /// <summary>The vertical room a subtree needs: its visible children stacked, or its own height when it has none to show.</summary>
    private static double SubtreeHeight(MindmapNode node, MindmapMetrics metrics, Func<MindmapNode, bool> isFolded, Func<MindmapNode, string> textOf)
    {
        var own = metrics.Measure(textOf(node)).Height;
        if (isFolded(node) || !node.HasChildren)
        {
            return own;
        }

        var children = node.Children;
        var stacked = children.Sum(child => SubtreeHeight(child, metrics, isFolded, textOf));
        for (var i = 0; i < children.Count - 1; i++)
        {
            stacked += metrics.GapBetween(metrics.Measure(textOf(children[i])).Width, metrics.Measure(textOf(children[i + 1])).Width);
        }

        return Math.Max(own, stacked);
    }

    private static string OwnText(MindmapNode node) => node.Text;
}
