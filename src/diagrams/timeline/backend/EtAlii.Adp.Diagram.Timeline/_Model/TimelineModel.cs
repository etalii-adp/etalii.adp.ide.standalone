namespace EtAlii.Adp.Diagram.Timeline;

/// <summary>
/// A parsed <c>.tml</c> document: what it declares, and where each declaration sits.
/// </summary>
/// <remarks>
/// A pure value. Every rule, every mapping and every layout decision is a function over this, so
/// each of them is testable from a string with no file, canvas or connection in sight.
/// </remarks>
/// <param name="Elements">The elements, in document order.</param>
/// <param name="Connections">The connections, in document order.</param>
public sealed record TimelineModel(
    IReadOnlyList<TimelineElement> Elements,
    IReadOnlyList<TimelineConnection> Connections)
{
    /// <summary>An empty model, for a document that declares nothing.</summary>
    public static TimelineModel Empty { get; } = new([], []);
}
