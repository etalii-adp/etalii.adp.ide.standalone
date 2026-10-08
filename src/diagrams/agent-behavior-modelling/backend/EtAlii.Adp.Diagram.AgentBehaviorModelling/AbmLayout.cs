using EtAlii.Adp.Hierarchy;

namespace EtAlii.Adp.Diagram.AgentBehaviorModelling;

/// <summary>
/// Where each node is drawn: a top-down tree, children left to right in the order the Markdown
/// lists them, each parent centred over its children.
/// </summary>
/// <remarks>
/// <para>
/// <b>Computed, because the order is the meaning.</b> In a behavior tree the left-to-right order
/// of a node's children is the order they are tried in, so a layout that let a child drift right
/// of its next sibling would draw a different program than the file holds. Every position comes
/// from the tree; an author's drag is an override kept in the <c>.adp</c>, laid over this one
/// node by node (core's <see cref="RegistrationLayout.Apply"/>).
/// </para>
/// <para>
/// <b>An author's drag moves a row, never a single box.</b> Across, a node's place is its order
/// among its siblings, so the computed x always holds; a drag sideways changes the order instead
/// (<see cref="AbmArrangement"/>). Down, every child of one parent sits at the same height, and the
/// <c>.adp</c> keeps the height a row was dragged to (<see cref="Arrange"/>); everything beneath a
/// row follows it, because each row below one that has no stored height hangs from its parent.
/// </para>
/// <para>
/// <b>Top-left corners</b>, as the registration stores them and the canvas sends them back.
/// </para>
/// </remarks>
public static class AbmLayout
{
    /// <summary>Every node's width.</summary>
    public const double NodeWidth = 200;

    /// <summary>Every node's height: the keyword line and the label line.</summary>
    public const double NodeHeight = 60;

    /// <summary>The space between two neighbouring subtrees.</summary>
    public const double HorizontalGap = 28;

    /// <summary>The space between a parent's bottom and its children's top.</summary>
    public const double VerticalGap = 56;

    /// <summary>The least space a dragged row keeps between its parent's bottom and its own top.</summary>
    public const double MinimumGap = 16;

    /// <summary>
    /// Where each node is drawn, the author's drags included: the computed x, and the height of
    /// its row - the first height stored for any node of the row, or the computed distance below
    /// its parent when none is.
    /// </summary>
    /// <remarks>
    /// A stored x is not used: the order is. And a row is never drawn closer to its parent than
    /// <see cref="MinimumGap"/>, so a hand-edited height cannot put a child above the node it runs under.
    /// </remarks>
    public static IReadOnlyDictionary<string, RegistrationPosition> Arrange(
        AbmModel model,
        IReadOnlyDictionary<string, RegistrationPosition> stored)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(stored);

        var computed = Compute(model);
        var positions = new Dictionary<string, RegistrationPosition>(StringComparer.Ordinal);
        PlaceRow(model, model.Roots, 0, double.NegativeInfinity, computed, stored, positions);
        return positions;
    }

    private static void PlaceRow(
        AbmModel model,
        IReadOnlyList<AbmNode> row,
        double hangingY,
        double floor,
        IReadOnlyDictionary<string, RegistrationPosition> computed,
        IReadOnlyDictionary<string, RegistrationPosition> stored,
        Dictionary<string, RegistrationPosition> positions)
    {
        var dragged = row.Select(node => stored.TryGetValue(node.Id, out var position) ? position.Y : (double?)null).FirstOrDefault(y => y is not null);
        var y = Math.Max(dragged ?? hangingY, floor);
        foreach (var node in row)
        {
            positions[node.Id] = new RegistrationPosition(computed[node.Id].X, y);
            PlaceRow(model, model.ChildrenOf(node), y + NodeHeight + VerticalGap, y + NodeHeight + MinimumGap, computed, stored, positions);
        }
    }

    /// <summary>The top-left of every node, by id, as the tree alone places it.</summary>
    /// <remarks>
    /// <para>
    /// <b>Tidy, not boxed.</b> Each subtree is placed as close to its left sibling as their
    /// <i>outlines</i> allow - the Reingold-Tilford rule - rather than as close as their bounding
    /// boxes allow: a shallow subtree tucks in under a deep neighbour's empty corner instead of
    /// pushing everything right of it out by the neighbour's full width. That is what keeps a wide
    /// behavior tree as narrow, and its parent lines as short, as the order permits.
    /// </para>
    /// <para>
    /// <b>The order is never traded for room</b>: children stay left to right in document order, a
    /// parent stays centred over its first and last child, and every depth is one row.
    /// </para>
    /// </remarks>
    public static IReadOnlyDictionary<string, RegistrationPosition> Compute(AbmModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        // Each node's left edge relative to its parent's, and each subtree's outline per depth
        // relative to its own root's left edge. Document order is depth-first, so walking it
        // backwards meets every child before its parent.
        var offsets = new Dictionary<string, double>(StringComparer.Ordinal);
        var outlines = new Dictionary<string, List<(double Left, double Right)>>(StringComparer.Ordinal);
        foreach (var node in model.Nodes.Reverse())
        {
            var (placed, outline) = Pack(node.ChildIds.Select(id => outlines[id]).ToList(), HorizontalGap);
            if (placed.Count == 0)
            {
                outlines[node.Id] = [(0, NodeWidth)];
                continue;
            }

            // Centred over the first and last child, whose roots sit at their packed offsets.
            var left = (placed[0] + placed[^1]) / 2;
            for (var index = 0; index < placed.Count; index++)
            {
                offsets[node.ChildIds[index]] = placed[index] - left;
            }

            outlines[node.Id] = [(0, NodeWidth), .. outline.Select(level => (level.Left - left, level.Right - left))];
        }

        // The roots side by side, packed the same way with a wider gap between whole trees.
        var roots = model.Roots;
        var (rootLefts, _) = Pack(roots.Select(root => outlines[root.Id]).ToList(), HorizontalGap * 2);
        var positions = new Dictionary<string, RegistrationPosition>(StringComparer.Ordinal);
        for (var index = 0; index < roots.Count; index++)
        {
            Place(model, roots[index], rootLefts[index], 0, offsets, positions);
        }

        // From the leftmost node at zero, so the drawing starts where it always has.
        var shift = positions.Count == 0 ? 0 : positions.Values.Min(position => position.X);
        return positions.ToDictionary(entry => entry.Key, entry => entry.Value with { X = entry.Value.X - shift }, StringComparer.Ordinal);
    }

    /// <summary>
    /// Places outlines left to right, each as far left as keeps <paramref name="gap"/> clear of
    /// everything before it at every depth they share; answers each one's offset and the combined outline.
    /// </summary>
    private static (List<double> Offsets, List<(double Left, double Right)> Outline) Pack(
        IReadOnlyList<List<(double Left, double Right)>> outlines, double gap)
    {
        var offsets = new List<double>();
        var combined = new List<(double Left, double Right)>();
        foreach (var outline in outlines)
        {
            var offset = 0d;
            if (combined.Count > 0)
            {
                offset = double.NegativeInfinity;
                for (var depth = 0; depth < Math.Min(combined.Count, outline.Count); depth++)
                {
                    offset = Math.Max(offset, combined[depth].Right + gap - outline[depth].Left);
                }
            }

            offsets.Add(offset);
            for (var depth = 0; depth < outline.Count; depth++)
            {
                (double left, double right) = (outline[depth].Left + offset, outline[depth].Right + offset);
                if (depth < combined.Count)
                {
                    combined[depth] = (Math.Min(combined[depth].Left, left), Math.Max(combined[depth].Right, right));
                }
                else
                {
                    combined.Add((left, right));
                }
            }
        }

        return (offsets, combined);
    }

    private static void Place(
        AbmModel model,
        AbmNode node,
        double left,
        int depth,
        Dictionary<string, double> offsets,
        Dictionary<string, RegistrationPosition> positions)
    {
        positions[node.Id] = new RegistrationPosition(left, depth * (NodeHeight + VerticalGap));
        foreach (var child in model.ChildrenOf(node))
        {
            Place(model, child, left + offsets[child.Id], depth + 1, offsets, positions);
        }
    }
}
