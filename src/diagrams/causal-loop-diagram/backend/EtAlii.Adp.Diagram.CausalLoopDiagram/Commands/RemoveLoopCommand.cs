using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.CausalLoopDiagram;

/// <summary>Withdraws a loop's claim - and nothing else. The links it named survive.</summary>
public sealed record RemoveLoopCommand(string BodyPath, string Identifier) : IDocumentEditCommand;

/// <inheritdoc cref="RemoveLoopCommand" />
public sealed class RemoveLoopCommandHandler(ICausalLoopDocumentStore documents) : CausalLoopEditHandler<RemoveLoopCommand>(documents)
{
    /// <inheritdoc />
    protected override EditOutcome Apply(CausalLoopBody body, CausalLoopModel model, RemoveLoopCommand command)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(command);

        return CausalLoopEdits.HasLoop(model, command.Identifier)
            ? CausalLoopEdits.Remove(body, CausalLoopEdits.LoopId(command.Identifier))
            : EditOutcome.Refused(CausalLoopEdits.NoSuchLoop);
    }
}
