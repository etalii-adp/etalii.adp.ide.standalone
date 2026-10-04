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
    public static IReadOnlyDictionary<string, RegistrationPosition> Compute(AbmModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var widths = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var node in Enumerable.Reverse(model.Nodes))
        {
            // Document order is depth-first, so walking it backwards meets every child before its parent.
            var children = node.ChildIds.Sum(id => widths[id]) + (HorizontalGap * Math.Max(0, node.ChildIds.Count - 1));
            widths[node.Id] = Math.Max(NodeWidth, children);
        }

        var positions = new Dictionary<string, RegistrationPosition>(StringComparer.Ordinal);
        var left = 0d;
        foreach (var root in model.Roots)
        {
            Place(model, root, left, 0, widths, positions);
            left += widths[root.Id] + (HorizontalGap * 2);
        }

        return positions;
    }

    private static void Place(
        AbmModel model,
        AbmNode node,
        double left,
        int depth,
        Dictionary<string, double> widths,
        Dictionary<string, RegistrationPosition> positions)
    {
        var span = widths[node.Id];
        positions[node.Id] = new RegistrationPosition(left + ((span - NodeWidth) / 2), depth * (NodeHeight + VerticalGap));

        var children = model.ChildrenOf(node);
        var childrenWidth = children.Sum(child => widths[child.Id]) + (HorizontalGap * Math.Max(0, children.Count - 1));
        var childLeft = left + ((span - childrenWidth) / 2);
        foreach (var child in children)
        {
            Place(model, child, childLeft, depth + 1, widths, positions);
            childLeft += widths[child.Id] + HorizontalGap;
        }
    }
}
