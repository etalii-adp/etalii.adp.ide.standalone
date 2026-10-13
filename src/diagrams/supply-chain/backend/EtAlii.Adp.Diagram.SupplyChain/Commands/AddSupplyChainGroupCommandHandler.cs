using EtAlii.Adp.Documents;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.SupplyChain;

/// <summary>Adds an empty group where it was dropped, placed so it draws before anything is moved into it.</summary>
public sealed class AddSupplyChainGroupCommandHandler(ISupplyChainDocumentStore documents) : SupplyChainEditHandler<AddSupplyChainGroupCommand>(documents)
{
    /// <inheritdoc />
    protected override CommandResult Execute(AddSupplyChainGroupCommand command) =>
        base.Execute(command.GroupId.Length > 0 ? command : command with { GroupId = ShortGuid.NewShortGuid().ToString() });

    /// <inheritdoc />
    protected override EditOutcome Apply(LineDocument document, SupplyChainModel model, AddSupplyChainGroupCommand command)
    {
        if (model.IsTaken(command.GroupId))
        {
            return EditOutcome.Refused("That id is already used in this diagram.");
        }

        // The drop is the centre; the document holds the top-left.
        var group = new SupplyChainGroup(
            command.GroupId,
            Unique(model.Groups.Select(existing => existing.Name), "New group"),
            Description: "",
            new LineRange(0, 0),
            X: command.X - (SupplyChainGeometry.EmptyGroupWidth / 2),
            Y: command.Y - (SupplyChainGeometry.EmptyGroupHeight / 2));

        return SupplyChainWriter.AddGroup(document, model, group);
    }
}
