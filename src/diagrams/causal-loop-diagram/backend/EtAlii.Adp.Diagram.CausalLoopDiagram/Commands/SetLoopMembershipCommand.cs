using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.CausalLoopDiagram;

/// <summary>Changes the cycle a loop claims.</summary>
public sealed record SetLoopMembershipCommand(string BodyPath, string Identifier, IReadOnlyList<string> Variables) : IDocumentEditCommand;

/// <inheritdoc cref="SetLoopMembershipCommand" />
public sealed class SetLoopMembershipCommandHandler(ICausalLoopDocumentStore documents) : CausalLoopEditHandler<SetLoopMembershipCommand>(documents)
{
    /// <inheritdoc />
    protected override EditOutcome Apply(CausalLoopBody body, CausalLoopModel model, SetLoopMembershipCommand command)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(command);

        if (CausalLoopEdits.MembershipRefusal(model, command.Variables) is { } refusal) return EditOutcome.Refused(refusal);
        return CausalLoopEdits.HasLoop(model, command.Identifier)
            ? CausalLoopEdits.Set(body, CausalLoopEdits.LoopId(command.Identifier), "Loop", ("members", CausalLoopEdits.Members(command.Variables)))
            : EditOutcome.Refused(CausalLoopEdits.NoSuchLoop);
    }
}
