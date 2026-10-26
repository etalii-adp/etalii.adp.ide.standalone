using EtAlii.Adp.Documents;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.SupplyChain;

/// <summary>
/// The + and − of a node or a flow: the definition's increase and decrease of its quantity or volume, once per step.
/// The value never goes below zero: a decrement at zero is refused with the definition's sentence.
/// </summary>
public sealed class StepSupplyChainValueCommandHandler(ISupplyChainDocumentStore documents) : SupplyChainEditHandler<StepSupplyChainValueCommand>(documents)
{
    /// <inheritdoc />
    protected override async Task<CommandResult> RunAsync(StepSupplyChainValueCommand command, CancellationToken cancellationToken) =>
        command.Steps == 0 ? CommandResult.Failure("A step of nothing changes nothing.") : await base.RunAsync(command, cancellationToken);

    /// <inheritdoc />
    protected override EditOutcome Apply(SupplyChainBody document, SupplyChainModel model, StepSupplyChainValueCommand command) =>
        SupplyChainDefinition.Step(document, model, command.EntryId, command.Steps) ?? Gone;
}
