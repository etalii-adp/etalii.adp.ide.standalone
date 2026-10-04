using EtAlii.Adp.Documents;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.SupplyChain;

/// <summary>A new group around one node - the way a group comes into being, since an empty one draws nothing.</summary>
public sealed class GroupSupplyChainNodeCommandHandler(ISupplyChainDocumentStore documents) : ICommandHandler<GroupSupplyChainNodeCommand>
{
    /// <inheritdoc />
    public Task<CommandResult> ExecuteAsync(GroupSupplyChainNodeCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(command.GroupName))
        {
            return Task.FromResult(CommandResult.Failure("A group needs a name."));
        }

        var minted = command.GroupId.Length > 0 ? command : command with { GroupId = ShortGuid.NewShortGuid().ToString() };

        return SupplyChainEdits.Run(documents, minted.BodyPath, minted, (document, model) =>
        {
            if (SupplyChainEdits.NodeOf(model, minted.NodeId) is not { } node)
            {
                return SupplyChainEdits.Gone();
            }

            if (SupplyChainEdits.IsTaken(model, minted.GroupId))
            {
                return SupplyChainEdit.Refused("That id is already used in this diagram.");
            }

            // The node's key first, then the group read afresh: adding the group moves every line
            // below the groups section, the node's among them.
            SupplyChainWriter.SetText(document, node.Range, SupplyChainKeys.Group, minted.GroupId, removeWhenEmpty: false);
            var group = new SupplyChainGroup(minted.GroupId, minted.GroupName.Trim(), Description: "", new LineRange(0, 0));
            return SupplyChainWriter.AddGroup(document, SupplyChainParser.Parse(document), group);
        });
    }
}
