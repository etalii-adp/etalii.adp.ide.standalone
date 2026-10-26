using EtAlii.Adp.Documents;
using EtAlii.Adp.History;
using EtAlii.Adp.Specification.Disl;

namespace EtAlii.Adp.Diagram.SupplyChain;

/// <summary>Adds an empty group where it was dropped, placed so it draws before anything is moved into it: the definition's <c>addGroupHere</c>.</summary>
public sealed class AddSupplyChainGroupCommandHandler(ISupplyChainDocumentStore documents) : SupplyChainEditHandler<AddSupplyChainGroupCommand>(documents)
{
    /// <inheritdoc />
    protected override async Task<CommandResult> RunAsync(AddSupplyChainGroupCommand command, CancellationToken cancellationToken) =>
        await base.RunAsync(command.GroupId.Length > 0 ? command : command with { GroupId = ShortGuid.NewShortGuid().ToString() }, cancellationToken);

    /// <inheritdoc />
    protected override EditOutcome Apply(SupplyChainBody document, SupplyChainModel model, AddSupplyChainGroupCommand command)
    {
        // The id is the host's, minted or asked for, so whether it is free is the host's to say.
        return model.IsTaken(command.GroupId)
            ? EditOutcome.Refused("That id is already used in this diagram.")
            : SupplyChainDefinition.Run(document, model, "addGroupHere", null, DislIds.Fixed(command.GroupId), SupplyChainDefinition.At(command.X, command.Y));
    }
}
