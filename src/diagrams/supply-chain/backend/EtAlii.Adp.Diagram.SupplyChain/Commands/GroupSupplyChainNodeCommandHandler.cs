using EtAlii.Adp.Documents;
using EtAlii.Adp.History;
using EtAlii.Adp.Specification.Disl;

namespace EtAlii.Adp.Diagram.SupplyChain;

/// <summary>
/// A new group around one node - the way a group comes into being, since an empty one draws nothing: the definition's
/// <c>putInNewGroup</c>, which writes the group and the node's key as one edit.
/// </summary>
public sealed class GroupSupplyChainNodeCommandHandler(ISupplyChainDocumentStore documents) : SupplyChainEditHandler<GroupSupplyChainNodeCommand>(documents)
{
    /// <inheritdoc />
    protected override async Task<CommandResult> RunAsync(GroupSupplyChainNodeCommand command, CancellationToken cancellationToken)
    {
        // The name is the command's own, so a blank one is refused before the document is read, as it always was; the
        // definition refuses it too, in the same sentence.
        return string.IsNullOrWhiteSpace(command.GroupName)
            ? CommandResult.Failure("A group needs a name.")
            : await base.RunAsync(command.GroupId.Length > 0 ? command : command with { GroupId = ShortGuid.NewShortGuid().ToString() }, cancellationToken);
    }

    /// <inheritdoc />
    protected override EditOutcome Apply(SupplyChainBody document, SupplyChainModel model, GroupSupplyChainNodeCommand command)
    {
        if (model.NodeOf(command.NodeId) is null)
        {
            return Gone;
        }

        // The id is the host's, minted or asked for, so whether it is free is the host's to say.
        return model.IsTaken(command.GroupId)
            ? EditOutcome.Refused("That id is already used in this diagram.")
            : SupplyChainDefinition.Run(
                document,
                model,
                "putInNewGroup",
                command.NodeId,
                DislIds.Fixed(command.GroupId),
                new DislInvocation(Parameters: new Dictionary<string, object?>(StringComparer.Ordinal) { ["groupName"] = command.GroupName }));
    }
}
