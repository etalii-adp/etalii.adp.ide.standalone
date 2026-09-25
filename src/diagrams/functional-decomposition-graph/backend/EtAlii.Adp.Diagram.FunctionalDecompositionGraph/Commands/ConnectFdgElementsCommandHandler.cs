using EtAlii.Adp.Documents;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.FunctionalDecompositionGraph;

/// <summary>
/// Links two elements - after checking the link the way the canvas does, because the request may not
/// have come from a canvas that checked.
/// </summary>
public sealed class ConnectFdgElementsCommandHandler(IFdgDocumentStore documents) : ICommandHandler<ConnectFdgElementsCommand>
{
    /// <inheritdoc />
    public Task<CommandResult> ExecuteAsync(ConnectFdgElementsCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        var minted = command.ConnectionId.Length > 0
            ? command
            : command with { ConnectionId = ShortGuid.NewShortGuid().ToString() };

        return FdgEdits.Run(documents, minted.BodyPath, minted, (document, model) =>
        {
            // Checked against the document as it is now, never against what the client believed.
            var refusal = FdgConnectVerdict.RefusalFor(model, minted.RelationType, minted.FromId, minted.ToId);
            if (refusal is not null)
            {
                return FdgEdit.Refused(refusal);
            }

            if (model.Elements.Any(element => element.Id == minted.ConnectionId) ||
                model.Connections.Any(connection => connection.Id == minted.ConnectionId))
            {
                return FdgEdit.Refused("That id is already used in this graph.");
            }

            var connection = new FdgConnection(
                minted.ConnectionId,
                minted.RelationType,
                minted.FromId,
                minted.ToId,
                Name: "",
                Description: "",
                Range: new LineRange(0, 0));

            return FdgWriter.Connect(document, model, connection);
        });
    }
}
