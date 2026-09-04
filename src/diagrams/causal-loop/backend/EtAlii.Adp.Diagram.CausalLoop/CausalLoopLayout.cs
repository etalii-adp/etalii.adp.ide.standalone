namespace EtAlii.Adp.Diagram.CausalLoop;

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

        for (var index = 0; index < model.Variables.Count; index++)
        {
            var variable = model.Variables[index];

            // Starting at the top and running clockwise, which is how a reader traces a loop.
            var angle = (2 * Math.PI * index / model.Variables.Count) - (Math.PI / 2);
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
}
