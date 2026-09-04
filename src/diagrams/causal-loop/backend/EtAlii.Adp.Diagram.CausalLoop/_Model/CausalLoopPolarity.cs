namespace EtAlii.Adp.Diagram.CausalLoop;

/// <summary>
/// What a causal link asserts about the direction of its effect.
/// </summary>
/// <remarks>
/// <para>
/// Three cases, and <see cref="Unstated"/> is a real one rather than a parsing accident. The
/// module's central check counts negative links around a cycle, so a link whose polarity nobody
/// wrote makes every loop through it <i>undecidable</i> - which is a different answer from
/// "reinforcing" and has to stay different. Defaulting an unmarked link to
/// <see cref="Positive"/> would fabricate the count and report a confident label for a diagram
/// that does not state one (causal-loop-diagram Requirement 3.6).
/// </para>
/// <para>
/// The notation writes these two ways and both are in live use: <c>+</c> and <c>-</c>, or
/// <c>s</c> and <c>o</c> for "same" and "opposite". They mean the same two things, and the
/// parser reads both into these cases (Requirement 2.2).
/// </para>
/// </remarks>
public enum CausalLoopPolarity
{
    /// <summary>No polarity was written. Not a default - an absence, and loops through it are undecidable.</summary>
    Unstated = 0,

    /// <summary>An increase in the cause produces an increase in the effect, all else equal. Written <c>+</c> or <c>s</c>.</summary>
    Positive,

    /// <summary>An increase in the cause produces a decrease in the effect, all else equal. Written <c>-</c> or <c>o</c>.</summary>
    Negative,
}
