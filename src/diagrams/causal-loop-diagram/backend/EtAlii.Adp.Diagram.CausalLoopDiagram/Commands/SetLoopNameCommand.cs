using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.CausalLoopDiagram;

/// <summary>Renames a loop, without touching what it runs through.</summary>
public sealed record SetLoopNameCommand(string BodyPath, string Identifier, string Name) : IDocumentEditCommand;

/// <inheritdoc cref="SetLoopNameCommand" />
public sealed class SetLoopNameCommandHandler(ICausalLoopDocumentStore documents) : CausalLoopEditHandler<SetLoopNameCommand>(documents)
{
    /// <inheritdoc />
    protected override EditOutcome Apply(CausalLoopBody body, CausalLoopModel model, SetLoopNameCommand command)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(command);

        if (!CausalLoopEdits.IsUsableText(command.Name)) return EditOutcome.Refused(CausalLoopEdits.UnusableText);
        return CausalLoopEdits.HasLoop(model, command.Identifier)
            ? CausalLoopEdits.Set(body, CausalLoopEdits.LoopId(command.Identifier), "Loop", ("title", command.Name))
            : EditOutcome.Refused(CausalLoopEdits.NoSuchLoop);
    }
}
