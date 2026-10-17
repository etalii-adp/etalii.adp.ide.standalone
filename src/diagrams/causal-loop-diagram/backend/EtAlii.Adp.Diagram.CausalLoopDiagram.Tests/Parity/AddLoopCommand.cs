using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.CausalLoopDiagram.Tests.Parity;

/// <summary>Claims a feedback loop through the named variables.</summary>
public sealed record AddLoopCommand(string BodyPath, string Identifier, string Name, IReadOnlyList<string> Variables) : IDocumentEditCommand;

/// <inheritdoc cref="AddLoopCommand" />
public sealed class AddLoopCommandHandler(ICausalLoopDocumentStore documents) : CausalLoopEditHandler<AddLoopCommand>(documents)
{
    /// <inheritdoc />
    protected override EditOutcome Apply(CausalLoopBody body, CausalLoopModel model, AddLoopCommand command)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(command);

        if (!CausalLoopEdits.IsUsableName(command.Identifier)) return EditOutcome.Refused(CausalLoopEdits.UnusableName);
        if (CausalLoopEdits.HasLoop(model, command.Identifier)) return EditOutcome.Refused(CausalLoopEdits.LoopTaken);
        if (!CausalLoopEdits.IsUsableText(command.Name)) return EditOutcome.Refused(CausalLoopEdits.UnusableText);
        if (CausalLoopEdits.MembershipRefusal(model, command.Variables) is { } refusal) return EditOutcome.Refused(refusal);
        return CausalLoopEdits.AddLoop(body, command.Identifier, command.Name, command.Variables);
    }
}
