using EtAlii.Adp.Documents;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph;

/// <summary>
/// Draws an influence - after checking it the way the canvas does, because the request may not have
/// come from a canvas that checked.
/// </summary>
public sealed class AddGhgInfluenceCommandHandler(IGhgDocumentStore documents) : ICommandHandler<AddGhgInfluenceCommand>
{
    /// <inheritdoc />
    public Task<CommandResult> ExecuteAsync(AddGhgInfluenceCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        var minted = command.InfluenceId.Length > 0
            ? command
            : command with { InfluenceId = ShortGuid.NewShortGuid().ToString() };

        return GhgEdits.Run(documents, minted.BodyPath, minted, (document, model) =>
        {
            // Checked against the document as it is now, never against what the client believed.
            if (RefusalFor(model, minted.FromId, minted.ToId) is { } refusal)
            {
                return GhgEdit.Refused(refusal);
            }

            if (GhgEdits.IdTaken(model, minted.InfluenceId))
            {
                return GhgEdit.Refused("That id is already used in this graph.");
            }

            // A trigger has no phases, so an influence leaving one states no end at all.
            var fromEnd = GhgEdits.TrendOf(model, minted.FromId) is { } from
                ? minted.FromEnd ?? new GhgEnd(GhgPhases.Names[from.VisiblePhases - 1], GhgEnd.Bottom, 0.5)
                : GhgEnd.None;
            var influence = new GhgInfluence(
                minted.InfluenceId,
                minted.FromId,
                fromEnd,
                minted.ToId,
                minted.ToEnd ?? new GhgEnd(GhgPhases.Names[0], GhgEnd.Top, 0.5),
                Description: "",
                Range: new LineRange(0, 0));

            return GhgWriter.AddInfluence(document, model, influence);
        });
    }

    /// <summary>
    /// Why an influence from <paramref name="from"/> to <paramref name="to"/> may not be drawn, or null
    /// when it may. The same checks the canvas's relation type declares - a trend or trigger as the
    /// source, only a trend as the target - so a request the canvas would not offer is refused here too.
    /// </summary>
    public static string? RefusalFor(GhgModel model, string from, string to)
    {
        ArgumentNullException.ThrowIfNull(model);

        if (GhgEdits.TriggerOf(model, to) is not null && GhgEdits.TrendOf(model, to) is null)
        {
            return "An influence cannot end at a trigger.";
        }

        if ((GhgEdits.TrendOf(model, from) is null && GhgEdits.TriggerOf(model, from) is null) || GhgEdits.TrendOf(model, to) is null)
        {
            return "An influence is drawn from one trend to another.";
        }

        if (string.Equals(from, to, StringComparison.Ordinal))
        {
            return "A trend cannot influence itself.";
        }

        // Every influence in the document counts, including one hidden by a phase count
        // (Requirement 7.3): the check reads the document, not what is drawn.
        return GhgRuleSet.AlreadyInfluences(model, from, to)
            ? "This trend already influences that one; a trend influences another once in each direction."
            : null;
    }
}
