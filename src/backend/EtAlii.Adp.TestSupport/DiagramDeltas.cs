using EtAlii.Adp.Diagram;

namespace EtAlii.Adp.TestSupport;

/// <summary>What a test reads off a session's deltas: the elements of the one Add a baseline is.</summary>
public static class DiagramDeltas
{
    /// <summary>
    /// The elements of <paramref name="deltas"/> when they are exactly one Add, as a baseline is;
    /// anything else throws, naming what arrived instead.
    /// </summary>
    public static IReadOnlyList<DiagramElement> AddedElements(this IReadOnlyList<DiagramDelta> deltas) =>
        deltas is [DiagramAddDelta add]
            ? add.Elements
            : throw new InvalidOperationException(
                $"Expected exactly one Add delta, not {deltas.Count}: [{string.Join(", ", deltas.Select(delta => delta.GetType().Name))}].");

    /// <summary>The elements of <paramref name="session"/>'s baseline, which is one Add.</summary>
    public static IReadOnlyList<DiagramElement> BaselineElements(this IDiagramSession session) => session.Baseline().AddedElements();
}
