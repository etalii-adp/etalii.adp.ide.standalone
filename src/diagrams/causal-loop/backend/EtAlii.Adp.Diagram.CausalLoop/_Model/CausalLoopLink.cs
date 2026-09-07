using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.CausalLoop;

/// <summary>
/// One causal link: an arrow from a cause to an effect, carrying what it asserts about the
/// direction of that effect.
/// </summary>
/// <remarks>
/// It has no position of its own and never will. A link is drawn between its endpoints, which
/// is why the viewport filters it structurally - on whether both endpoints survived - rather
/// than by testing coordinates it does not have (Requirement 7.3).
/// </remarks>
/// <param name="From">The cause variable's id.</param>
/// <param name="To">The effect variable's id.</param>
/// <param name="Polarity">What the link asserts; <see cref="CausalLoopPolarity.Unstated"/> where nobody wrote it.</param>
/// <param name="Delayed">Whether the effect is marked as delayed - drawn as strokes across the arrow.</param>
/// <param name="Flipped">Whether the arc bows to the other side of its chord. The module chooses a side for every link, consistently, so that a two-variable loop draws as an ellipse; this is the author overriding that choice for one link, which is what untangles a crossing where the automatic side happens to read badly.</param>
/// <param name="Weight">An author's annotation of strength. Recorded, never evaluated: this diagram states structure and simulates nothing (Requirement 8.3).</param>
/// <param name="Label">An optional note on the link.</param>
/// <param name="Lines">Where the declaration sits.</param>
public sealed record CausalLoopLink(
    string From,
    string To,
    CausalLoopPolarity Polarity,
    bool Delayed,
    bool Flipped,
    double? Weight,
    string Label,
    LineRange Lines)
{
    /// <summary>The stable identity a selection and a delta address it by.</summary>
    public string Id => $"link:{From}|{To}";
}
