namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph;

/// <summary>
/// The four phases, and the one place their boundaries are computed (design, <i>Phase boundaries,
/// computed once</i>).
/// </summary>
/// <remarks>
/// <para>
/// <b>The rule lives here and nowhere else.</b> The mapper sends its result as fractions, so the
/// client never recomputes it; the commands call it to know where the drawn boundaries are before
/// they move one; the rules call it to know what is drawn. Three copies of "spread evenly between the
/// nearest dragged neighbours" would be three chances to disagree about a trend with one dragged
/// boundary, which is exactly the case the guard is seen to fail against.
/// </para>
/// <para>
/// <b>Months, not fractions, are what is stored and computed.</b> Every boundary this returns is
/// snapped to a month, because a drawn boundary is a date a reader reads off the axis.
/// </para>
/// </remarks>
public static class GhgPhases
{
    /// <summary>How many phases a trend can have.</summary>
    public const int Count = 4;

    /// <summary>The phases as the document names them, in order.</summary>
    public static readonly IReadOnlyList<string> Names = ["peak", "trough", "slope", "plateau"];

    /// <summary>The phases as a reader names them, in order.</summary>
    public static readonly IReadOnlyList<string> Titles = ["Peak", "Trough", "Slope", "Plateau"];

    /// <summary>The phases' full Gartner names, the canvas's tooltips (Requirement 4.4).</summary>
    public static readonly IReadOnlyList<string> GartnerNames =
    [
        "Peak of Inflated Expectations",
        "Trough of Disillusionment",
        "Slope of Enlightenment",
        "Plateau of Productivity",
    ];

    /// <summary>
    /// The document key of each inner boundary: the end of the phase it closes. There are three, one
    /// fewer than the phases.
    /// </summary>
    public static readonly IReadOnlyList<string> BoundaryKeys = ["peak-end", "trough-end", "slope-end"];

    /// <summary>The phase's index, or -1 when <paramref name="name"/> names none.</summary>
    public static int IndexOf(string? name)
    {
        for (var index = 0; index < Names.Count; index++)
        {
            if (string.Equals(Names[index], name, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>
    /// The drawn inner boundaries of <paramref name="trend"/>, as month indices: <c>VisiblePhases - 1</c>
    /// of them. Empty for a trend that cannot be drawn.
    /// </summary>
    /// <remarks>
    /// The design's three rules, in order:
    /// <list type="number">
    /// <item>The last visible phase ends at <c>stop</c>.</item>
    /// <item>Each inner boundary before the last visible phase is its dragged date when one is stored,
    /// otherwise it is spread evenly between its nearest dragged neighbours or the trend's ends, then
    /// snapped to a month.</item>
    /// <item>A dragged boundary at or beyond the last visible phase is kept in the document and not
    /// drawn - and so it is not a neighbour either.</item>
    /// </list>
    /// A stored boundary outside the span, or out of order, is clamped for drawing only; the rules
    /// report it as <c>ghg.boundary-order</c>.
    /// </remarks>
    public static IReadOnlyList<int> BoundariesOf(GhgTrend trend)
    {
        ArgumentNullException.ThrowIfNull(trend);
        return trend.HasSpan
            ? BoundariesOf(trend.Start!.Value, trend.Stop!.Value, trend.VisiblePhases, trend.DraggedEnds)
            : [];
    }

    /// <summary>The drawn inner boundaries of a span, as <see cref="BoundariesOf(GhgTrend)"/> computes them.</summary>
    public static IReadOnlyList<int> BoundariesOf(int start, int stop, int phases, IReadOnlyList<int?> dragged)
    {
        ArgumentNullException.ThrowIfNull(dragged);

        var inner = Math.Clamp(phases, 1, Count) - 1;
        var result = new int[inner];

        // The anchors: the start at position -1, each dragged boundary before the last visible phase
        // at its own position, and the stop at position `inner`.
        var anchorIndex = -1;
        var anchorValue = start;
        for (var index = 0; index <= inner; index++)
        {
            int? stored = index < inner && index < dragged.Count ? dragged[index] : null;
            if (index < inner && stored is null)
            {
                continue;
            }

            var value = index == inner ? stop : Math.Clamp(stored!.Value, anchorValue, stop);

            // Every boundary between the previous anchor and this one is spread evenly between them.
            for (var between = anchorIndex + 1; between < index; between++)
            {
                var exact = anchorValue + ((value - anchorValue) * (double)(between - anchorIndex) / (index - anchorIndex));
                result[between] = (int)Math.Round(exact, MidpointRounding.AwayFromZero);
            }

            if (index < inner)
            {
                result[index] = value;
            }

            anchorIndex = index;
            anchorValue = value;
        }

        return result;
    }

    /// <summary>The drawn boundaries of <paramref name="trend"/> as fractions of its width.</summary>
    public static IReadOnlyList<double> FractionsOf(GhgTrend trend)
    {
        ArgumentNullException.ThrowIfNull(trend);
        if (!trend.HasSpan)
        {
            return [];
        }

        var start = trend.Start!.Value;
        var months = (double)trend.Months;
        return [.. BoundariesOf(trend).Select(boundary => (boundary - start) / months)];
    }

    /// <summary>
    /// The stored boundaries after a MOVE by <paramref name="months"/>: shifted exactly, as
    /// <c>start</c> and <c>stop</c> are (Requirement 3.5).
    /// </summary>
    public static IReadOnlyList<int?> Moved(IReadOnlyList<int?> dragged, int months)
    {
        ArgumentNullException.ThrowIfNull(dragged);
        return [.. dragged.Select(boundary => boundary + months)];
    }

    /// <summary>
    /// The stored boundaries after a RESIZE from one span to another: each offset from the start is
    /// scaled by the new span over the old, rounded to a month, and every phase kept at least a month
    /// long (Requirement 3.5).
    /// </summary>
    /// <remarks>
    /// <b>Scaled, never kept absolute.</b> Keeping the dates as they were would leave a boundary outside
    /// a span that shrank past it - which is the defect task 15's guard is seen to fail against.
    /// The caller has already refused a span shorter than the visible phase count in months, so there
    /// is always room for every phase to be a month long.
    /// </remarks>
    public static IReadOnlyList<int?> Rescaled(
        IReadOnlyList<int?> dragged, int oldStart, int oldStop, int newStart, int newStop, int phases)
    {
        ArgumentNullException.ThrowIfNull(dragged);

        var oldSpan = Math.Max(1, oldStop - oldStart);
        var newSpan = newStop - newStart;
        var scaled = dragged
            .Select(boundary => boundary is { } value
                ? newStart + (int)Math.Round((value - oldStart) * (double)newSpan / oldSpan, MidpointRounding.AwayFromZero)
                : (int?)null)
            .ToArray();

        return KeptAMonthApart(scaled, newStart, newStop, phases);
    }

    /// <summary>
    /// The stored boundaries with <paramref name="index"/> set to <paramref name="month"/>, clamped so
    /// that no phase - including the ones spread evenly around it - becomes shorter than a month
    /// (Requirement 4.6).
    /// </summary>
    public static IReadOnlyList<int?> WithBoundary(
        IReadOnlyList<int?> dragged, int index, int month, int start, int stop, int phases)
    {
        ArgumentNullException.ThrowIfNull(dragged);

        var slots = Slots(dragged);
        slots[index] = month;
        return KeptAMonthApart(slots, start, stop, phases, pinned: index);
    }

    /// <summary>
    /// Clamps each stored boundary so every phase is at least a month long. Visible boundaries are
    /// clamped between their neighbouring anchors with room for the phases between; a boundary beyond
    /// the last visible phase is only kept inside the span.
    /// </summary>
    private static int?[] KeptAMonthApart(IReadOnlyList<int?> stored, int start, int stop, int phases, int pinned = -1)
    {
        var slots = Slots(stored);
        var inner = Math.Clamp(phases, 1, Count) - 1;

        // The pinned boundary first, against the anchors either side of it, so the others make room
        // for it rather than the other way round.
        if (pinned >= 0 && pinned < inner && slots[pinned] is { } moved)
        {
            var (lowIndex, low) = PreviousAnchor(slots, pinned, start);
            var (highIndex, high) = NextAnchor(slots, pinned, inner, stop);
            var lowest = low + (pinned - lowIndex);
            slots[pinned] = Math.Clamp(moved, lowest, Math.Max(lowest, high - (highIndex - pinned)));
        }

        var previousIndex = -1;
        var previous = start;
        for (var index = 0; index < slots.Length; index++)
        {
            if (slots[index] is not { } value)
            {
                continue;
            }

            if (index < inner)
            {
                var lower = previous + (index - previousIndex);
                var upper = stop - (inner - index);
                slots[index] = Math.Clamp(value, lower, Math.Max(lower, upper));
            }
            else
            {
                // Not drawn: kept, and kept inside the span and after the boundaries before it.
                var highest = Math.Max(start, stop - 1);
                slots[index] = Math.Clamp(value, Math.Min(previous + 1, highest), highest);
            }

            previousIndex = index;
            previous = slots[index]!.Value;
        }

        return slots;
    }

    private static (int Index, int Value) PreviousAnchor(int?[] slots, int from, int start)
    {
        for (var index = from - 1; index >= 0; index--)
        {
            if (slots[index] is { } value)
            {
                return (index, value);
            }
        }

        return (-1, start);
    }

    private static (int Index, int Value) NextAnchor(int?[] slots, int from, int inner, int stop)
    {
        for (var index = from + 1; index < inner; index++)
        {
            if (slots[index] is { } value)
            {
                return (index, value);
            }
        }

        return (inner, stop);
    }

    private static int?[] Slots(IReadOnlyList<int?> stored)
    {
        var slots = new int?[Count - 1];
        for (var index = 0; index < slots.Length && index < stored.Count; index++)
        {
            slots[index] = stored[index];
        }

        return slots;
    }
}
