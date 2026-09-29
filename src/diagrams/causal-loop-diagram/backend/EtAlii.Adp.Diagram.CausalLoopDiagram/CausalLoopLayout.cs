namespace EtAlii.Adp.Diagram.CausalLoopDiagram;

/// <summary>
/// Where a causal loop diagram's variables go when nobody has arranged them: evenly around a
/// ring, in the order the document declares them.
/// </summary>
/// <remarks>
/// <para>
/// <b>A ring, because this notation's subject is cycles.</b> Every other default in the catalog
/// is a layering of some kind, and layering needs an acyclic graph - which is precisely what a
/// causal loop diagram is not. A ring makes no such demand, draws a feedback loop as the loop it
/// is, and gives the reader the one shape the notation is about before any arrangement is
/// authored.
/// </para>
/// <para>
/// It also satisfies two of the inherited obligations by construction rather than by effort. The
/// extent is square, so the ratio bound is met at 1:1 whatever the document contains. And the
/// radius is chosen from the widest variable and the count, so no two neighbours can overlap -
/// the ring grows to fit rather than the boxes being pushed apart afterwards.
/// </para>
/// <para>
/// <b>This is not the self-organizing layout.</b> That one is invoked by the user, writes
/// authored positions, and lands in its own group. This is what a document opens with, and it
/// never writes anything.
/// </para>
/// </remarks>
public static class CausalLoopLayout
{
    /// <summary>
    /// Places every variable the model declares.
    /// </summary>
    /// <remarks>
    /// Returns a dictionary rather than a lookup with a fallback, so a caller asking for a
    /// variable that was not placed gets nothing back and has to say what it wants to do about
    /// it. That is Requirement 7.1: an absent position must be distinguishable from a position
    /// of <c>(0, 0)</c>, and the only way to keep that true is to never invent one here.
    /// </remarks>
    public static IReadOnlyDictionary<string, CausalLoopBox> Compute(
        CausalLoopModel model, CausalLoopMetrics? metrics = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        metrics ??= CausalLoopMetrics.Default;

        var boxes = new Dictionary<string, CausalLoopBox>(model.Variables.Count, StringComparer.Ordinal);
        if (model.Variables.Count == 0)
        {
            return boxes;
        }

        var height = metrics.Height;
        var widths = model.Variables.ToDictionary(
            variable => variable.Id,
            variable => metrics.WidthOf(variable.Display),
            StringComparer.Ordinal);

        // One variable sits at the origin. That is a real position rather than a missing one,
        // and the distinction matters enough to be tested: Requirement 7.1 requires an element
        // genuinely placed at (0, 0) to stay distinguishable from an unplaced one.
        if (model.Variables.Count == 1)
        {
            var only = model.Variables[0];
            boxes[only.Id] = new CausalLoopBox(0, 0, widths[only.Id], height);
            return boxes;
        }

        // The ring is sized so that consecutive boxes cannot touch: give each variable an arc
        // wide enough for the widest box plus the separation, and solve for the radius that
        // provides it. Growing the ring is what makes non-overlap structural here rather than
        // something a later pass has to repair.
        var slot = widths.Values.Max() + metrics.Separation;
        var radius = Math.Max(slot, model.Variables.Count * slot / (2 * Math.PI));

        // Around the ring in the order the links run, not the order the document declares.
        // Declaration order is the author's writing order and says nothing about structure, so a
        // ring built from it puts linked variables opposite each other and every link becomes a
        // chord across the middle. Following the links instead puts most of them along the rim.
        var order = RingOrder(model);

        for (var index = 0; index < order.Count; index++)
        {
            var variable = order[index];

            // Starting at the top and running clockwise, which is how a reader traces a loop.
            var angle = (2 * Math.PI * index / order.Count) - (Math.PI / 2);
            var centerX = radius * Math.Cos(angle);
            var centerY = radius * Math.Sin(angle);

            boxes[variable.Id] = new CausalLoopBox(
                centerX - (widths[variable.Id] / 2),
                centerY - (height / 2),
                widths[variable.Id],
                height);
        }

        return boxes;
    }

    /// <summary>
    /// The order variables are placed around the ring: a walk of the link graph rather than the
    /// order the document declares them in.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why this is not cosmetic.</b> A causal loop diagram's subject is its cycles, and a
    /// cycle is legible only when its variables are adjacent. Placed in declaration order, a
    /// four-variable loop written as <c>a, b, c, d</c> but linked <c>a to c to b to d</c> draws
    /// two chords straight through the middle of the ring. Following the links puts those same
    /// four in sequence, and the loop becomes the rim.
    /// </para>
    /// <para>
    /// <b>Deterministic, like everything else that decides a position here.</b> The walk starts
    /// at the first declared variable and, at every step, prefers the earliest-declared neighbour
    /// not yet placed; when a component is exhausted it continues from the earliest-declared
    /// variable still left. No random source, no wall-clock, and the same document always gives
    /// the same ring.
    /// </para>
    /// <para>
    /// It reads links as undirected for the same reason the self-organizing layout does: two
    /// variables joined by an arrow belong beside each other whichever way it points.
    /// </para>
    /// </remarks>
    internal static IReadOnlyList<CausalLoopVariable> RingOrder(CausalLoopModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var position = new Dictionary<string, int>(model.Variables.Count, StringComparer.Ordinal);
        for (var index = 0; index < model.Variables.Count; index++)
        {
            position[model.Variables[index].Id] = index;
        }

        var neighbours = new List<int>[model.Variables.Count];
        for (var index = 0; index < neighbours.Length; index++)
        {
            neighbours[index] = [];
        }

        foreach (var link in model.Links)
        {
            if (!position.TryGetValue(link.From, out var from)
                || !position.TryGetValue(link.To, out var to)
                || from == to)
            {
                continue;
            }

            neighbours[from].Add(to);
            neighbours[to].Add(from);
        }

        // Earliest-declared first at every branch, so the walk is a pure function of the document.
        foreach (var list in neighbours)
        {
            list.Sort();
        }

        var placed = new bool[model.Variables.Count];
        var order = new List<CausalLoopVariable>(model.Variables.Count);

        for (var seed = 0; seed < model.Variables.Count; seed++)
        {
            if (placed[seed])
            {
                continue;
            }

            // A depth-first walk keeps a cycle contiguous, where a breadth-first one would
            // interleave the branches hanging off it and break the rim apart again.
            var stack = new Stack<int>();
            stack.Push(seed);

            while (stack.Count > 0)
            {
                var current = stack.Pop();
                if (placed[current])
                {
                    continue;
                }

                placed[current] = true;
                order.Add(model.Variables[current]);

                // Pushed in reverse so the earliest-declared neighbour is popped first.
                for (var index = neighbours[current].Count - 1; index >= 0; index--)
                {
                    if (!placed[neighbours[current][index]])
                    {
                        stack.Push(neighbours[current][index]);
                    }
                }
            }
        }

        return order;
    }
}
