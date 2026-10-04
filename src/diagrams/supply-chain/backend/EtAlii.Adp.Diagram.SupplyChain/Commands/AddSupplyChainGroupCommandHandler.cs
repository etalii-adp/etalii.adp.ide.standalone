using EtAlii.Adp.Documents;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.SupplyChain;

/// <summary>Adds an empty group where it was dropped, placed so it draws before anything is moved into it.</summary>
public sealed class AddSupplyChainGroupCommandHandler(ISupplyChainDocumentStore documents) : ICommandHandler<AddSupplyChainGroupCommand>
{
    /// <inheritdoc />
    public Task<CommandResult> ExecuteAsync(AddSupplyChainGroupCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        var minted = command.GroupId.Length > 0 ? command : command with { GroupId = ShortGuid.NewShortGuid().ToString() };

        return SupplyChainEdits.Run(documents, minted.BodyPath, minted, (document, model) =>
        {
            if (SupplyChainEdits.IsTaken(model, minted.GroupId))
            {
                return SupplyChainEdit.Refused("That id is already used in this diagram.");
            }

            // The drop is the centre; the document holds the top-left.
            var group = new SupplyChainGroup(
                minted.GroupId,
                SupplyChainEdits.Unique(model.Groups.Select(existing => existing.Name), "New group"),
                Description: "",
                new LineRange(0, 0),
                X: minted.X - (SupplyChainGeometry.EmptyGroupWidth / 2),
                Y: minted.Y - (SupplyChainGeometry.EmptyGroupHeight / 2));

            return SupplyChainWriter.AddGroup(document, model, group);
        });
    }
}
