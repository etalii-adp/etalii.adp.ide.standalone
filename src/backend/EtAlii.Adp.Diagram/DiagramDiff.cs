namespace EtAlii.Adp.Diagram;

/// <summary>
/// The deltas between what a connection was last given and what it would be given now - written
/// once for every module that works them out itself (backend-centralization R4).
/// </summary>
/// <remarks>
/// <para>
/// <b>What is new or changed is added, what is gone is removed</b> (R4.1). An edited element keeps
/// its id, so it arrives as an add carrying its new state; the client folds an add as an upsert
/// keyed on id.
/// </para>
/// <para>
/// <b>Removals come first, then additions</b> (R4.5). The client's fold is why: removing first
/// can never delete something the same batch just added. The reverse order relies on the two id
/// sets being disjoint, which is true today and TRUE ONLY BY ACCIDENT - nothing in the code
/// enforces it. So the order is not a style choice, and it should not be reversed to match the
/// six copies that added first.
/// </para>
/// <para>
/// <b>Equality is equal position, type and payload BYTES</b> (R4.4), which is
/// <see cref="Same"/> and not the record's own equality: <see cref="ReadOnlyMemory{T}"/> compares
/// which array it points into rather than what the array holds, so two renderings of an
/// unchanged element would never compare equal and every change would resend everything.
/// </para>
/// <para>
/// An id that appears twice in one rendering is an error in the module that rendered it, and
/// this throws on it exactly as the copies it replaces did, rather than choosing one of the two.
/// </para>
/// </remarks>
public static class DiagramDiff
{
    /// <summary>
    /// The deltas that turn <paramref name="before"/> into <paramref name="after"/>: at most one
    /// remove, then at most one add, and nothing at all when the two say the same thing.
    /// </summary>
    public static IReadOnlyList<DiagramDelta> Between(
        IReadOnlyList<DiagramElement> before,
        IReadOnlyList<DiagramElement> after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        var beforeById = before.ToDictionary(element => element.Id, StringComparer.Ordinal);
        var afterById = after.ToDictionary(element => element.Id, StringComparer.Ordinal);

        var gone = before
            .Where(element => !afterById.ContainsKey(element.Id))
            .Select(element => element.Id)
            .ToArray();
        var arrived = after
            .Where(element => !beforeById.TryGetValue(element.Id, out var was) || !Same(was, element))
            .ToArray();

        var deltas = new List<DiagramDelta>(2);
        if (gone.Length > 0)
        {
            deltas.Add(new DiagramRemoveDelta(gone));
        }

        if (arrived.Length > 0)
        {
            deltas.Add(new DiagramAddDelta(arrived));
        }

        return deltas;
    }

    /// <summary>
    /// Whether two renderings of one element say the same thing: equal position, type and
    /// payload bytes (R4.4). The payload's type URL is not compared: none of the copies did, and
    /// R4.4 does not ask for it.
    /// </summary>
    public static bool Same(DiagramElement left, DiagramElement right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        return left.X.Equals(right.X)
            && left.Y.Equals(right.Y)
            && string.Equals(left.Type, right.Type, StringComparison.Ordinal)
            && left.Payload.Span.SequenceEqual(right.Payload.Span);
    }
}
