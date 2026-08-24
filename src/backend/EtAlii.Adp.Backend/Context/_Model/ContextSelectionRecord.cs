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

    /// <summary>
    /// The one place backend context records become wire messages, shared by the service
    /// and the selection store so a pushed selection and a discovered action list always
    /// have the same shape.
    /// </summary>
    /// <param name="record">The context selection record to map.</param>
    /// <param name="rootActions">
    /// What applies when nothing is selected - the project root's actions. Carried on the
    /// "nothing selected" message so the explorer's empty space has a menu without a round
    /// trip; the selection itself stays absent, because nothing is selected.
    /// </param>
    /// <param name="transient">Whether the selection is transient.</param>
    public static ContextMessage ToWire(
        ContextSelectionRecord? record,
        IReadOnlyList<ContextActionGroupDefinition>? rootActions = null,
        bool transient = false)
    {
        var changed = new ContextSelectionChanged { Transient = transient };
        if (record is not null)
        {
            changed.Selection = record.Chain;
            changed.Levels.AddRange(record.Levels.Select(level => level.Detail));
            changed.Actions.AddRange(record.Actions.Select(ContextActionGroupDefinition.ToProto));
        }
        else if (rootActions is not null)
        {
            changed.Actions.AddRange(rootActions.Select(ContextActionGroupDefinition.ToProto));
        }

        return new ContextMessage { Selection = changed };
    }
}
