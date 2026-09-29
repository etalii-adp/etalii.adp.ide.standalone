using EtAlii.Adp.Documents;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph;

/// <summary>Adds a trigger, named <c>Trigger</c> and numbered when that is taken, with no tags.</summary>
public sealed class AddGhgTriggerCommandHandler(IGhgDocumentStore documents) : ICommandHandler<AddGhgTriggerCommand>
{
    /// <summary>A new trigger's name, numbered when it is taken.</summary>
    public const string DefaultName = "Trigger";

    /// <inheritdoc />
    public Task<CommandResult> ExecuteAsync(AddGhgTriggerCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        var minted = command.TriggerId.Length > 0
            ? command
            : command with { TriggerId = ShortGuid.NewShortGuid().ToString() };

        return GhgEdits.Run(documents, minted.BodyPath, minted, (document, model) =>
        {
            if (GhgEdits.IdTaken(model, minted.TriggerId))
            {
                return GhgEdit.Refused("That id is already used in this graph.");
            }

            var trigger = new GhgTrigger(
                minted.TriggerId,
                GhgEdits.UniqueName(model.Triggers.Select(existing => existing.Name), DefaultName),
                GhgScale.MonthContaining(minted.X, model.TimeUnit),
                GhgScale.RowAtMiddle(minted.Y),
                [],
                Description: "",
                Range: new LineRange(0, 0));

            return GhgWriter.AddTrigger(document, model, trigger);
        });
    }
}
