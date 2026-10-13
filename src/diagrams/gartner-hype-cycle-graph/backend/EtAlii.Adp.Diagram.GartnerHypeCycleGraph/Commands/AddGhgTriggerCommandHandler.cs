using EtAlii.Adp.History;
using EtAlii.Adp.Specification.Disl;

namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph;

/// <summary>Adds a trigger, named <c>Trigger</c> and numbered when that is taken, with no tags.</summary>
public sealed class AddGhgTriggerCommandHandler(IGhgDocumentStore documents) : GhgEditHandler<AddGhgTriggerCommand>(documents)
{
    /// <summary>A new trigger's name, numbered when it is taken.</summary>
    public const string DefaultName = "Trigger";

    /// <inheritdoc />
    protected override CommandResult Execute(AddGhgTriggerCommand command)
    {
        return base.Execute(command.TriggerId.Length > 0 ? command : command with { TriggerId = ShortGuid.NewShortGuid().ToString() });
    }

    /// <inheritdoc />
    protected override EditOutcome Apply(GhgBody document, GhgModel model, AddGhgTriggerCommand command)
    {
        if (model.IsTaken(command.TriggerId))
        {
            return EditOutcome.Refused("That id is already used in this graph.");
        }

        return GhgDefinition.Apply(document, OperationInterpreter.Run(
            GhgDefinition.Specification,
            "addTriggerHere",
            document.Disl.Diagram,
            null,
            DislIds.Fixed(command.TriggerId),
            new DislInvocation(GhgDefinition.Position(command.X, command.Y, model.TimeUnit)),
            GhgDefinition.EditEnv));
    }
}
