namespace EtAlii.Adp.Diagram.CausalLoopDiagram;

/// <summary>What a <c>.cld</c> document states: its variables, its causal links, and the loops it claims.</summary>
/// <remarks>
/// Deliberately only what is <i>written</i>. The cycles the graph actually contains, and whether
/// each is reinforcing, are computed from this rather than stored in it - so the document's
/// claims and the arithmetic can be compared instead of one silently becoming the other.
/// </remarks>
/// <param name="Variables">In document order.</param>
/// <param name="Links">In document order.</param>
/// <param name="Loops">In document order.</param>
public sealed record CausalLoopModel(
    IReadOnlyList<CausalLoopVariable> Variables,
    IReadOnlyList<CausalLoopLink> Links,
    IReadOnlyList<CausalLoopLoop> Loops)
{
    /// <summary>The empty model an unreadable or empty document yields.</summary>
    public static CausalLoopModel Empty { get; } = new([], [], []);

    /// <summary>Whether the document declares the variable this id names.</summary>
    public bool Declares(string id) => Variables.Any(variable => string.Equals(variable.Id, id, StringComparison.Ordinal));
}
