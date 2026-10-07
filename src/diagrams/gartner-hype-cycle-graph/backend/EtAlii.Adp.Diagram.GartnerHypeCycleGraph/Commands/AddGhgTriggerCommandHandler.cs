using EtAlii.Adp.History;
using EtAlii.Adp.Specification.Disl;

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

            return GhgDefinition.Apply(document, OperationInterpreter.Run(
                GhgDefinition.Specification,
                "addTriggerHere",
                document.Disl.Diagram,
                null,
                DislIds.Fixed(minted.TriggerId),
                new DislInvocation(GhgDefinition.Position(minted.X, minted.Y, model.TimeUnit)),
                GhgDefinition.EditEnv));
        });
    }
}
