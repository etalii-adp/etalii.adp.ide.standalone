using EtAlii.Adp.Documents;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.SupplyChain;

/// <summary>A new group around one node - the way a group comes into being, since an empty one draws nothing.</summary>
public sealed class GroupSupplyChainNodeCommandHandler(ISupplyChainDocumentStore documents) : SupplyChainEditHandler<GroupSupplyChainNodeCommand>(documents)
{
    /// <inheritdoc />
    protected override CommandResult Execute(GroupSupplyChainNodeCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.GroupName))
        {
            return CommandResult.Failure("A group needs a name.");
        }

        return base.Execute(command.GroupId.Length > 0 ? command : command with { GroupId = ShortGuid.NewShortGuid().ToString() });
    }

    /// <inheritdoc />
    protected override EditOutcome Apply(LineDocument document, SupplyChainModel model, GroupSupplyChainNodeCommand command)
    {
        if (model.NodeOf(command.NodeId) is not { } node)
        {
            return Gone;
        }

        if (model.IsTaken(command.GroupId))
        {
            return EditOutcome.Refused("That id is already used in this diagram.");
        }

        // The node's key first, then the group read afresh: adding the group moves every line
        // below the groups section, the node's among them.
        SupplyChainWriter.SetText(document, node.Range, SupplyChainKeys.Group, command.GroupId, removeWhenEmpty: false);
        var group = new SupplyChainGroup(command.GroupId, command.GroupName.Trim(), Description: "", new LineRange(0, 0));
        return SupplyChainWriter.AddGroup(document, SupplyChainParser.Parse(document), group);
    }
}
