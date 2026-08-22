namespace EtAlii.Adp.Backend.Context;

/// <summary>
/// A connection's recorded selection: the chain as accepted (paths filled in), every
/// level resolved, the innermost level's actions, and the subscriptions that keep the
/// record current while the things it names change underneath it.
/// </summary>
/// <param name="Chain">The selection as the client will see it echoed back.</param>
/// <param name="Levels">Outermost first; one per level of <paramref name="Chain"/>.</param>
/// <param name="Actions">The innermost level's currently available actions.</param>
/// <param name="Action">The gesture at the innermost level, if any.</param>
/// <param name="Tracks">Observations of each level; disposed when the record is replaced.</param>
public sealed record ContextSelectionRecord(
    ContextSelection Chain,
    IReadOnlyList<ContextResolvedLevel> Levels,
    IReadOnlyList<ContextActionGroupDefinition> Actions,
    ContextSelectionAction? Action,
    IReadOnlyList<IDisposable> Tracks)
{
    /// <summary>The level the user actually acted on.</summary>
    public ContextResolvedLevel Innermost => Levels[^1];
}
