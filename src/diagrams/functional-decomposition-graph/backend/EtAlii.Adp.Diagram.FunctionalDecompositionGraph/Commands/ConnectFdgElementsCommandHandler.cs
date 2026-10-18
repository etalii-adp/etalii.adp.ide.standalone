using EtAlii.Adp.Documents;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.FunctionalDecompositionGraph;

/// <summary>
/// Links two elements - after checking the link the way the canvas does, because the request may not
/// have come from a canvas that checked.
/// </summary>
public sealed class ConnectFdgElementsCommandHandler(IFdgDocumentStore documents) : FdgEditHandler<ConnectFdgElementsCommand>(documents)
{
    /// <inheritdoc />
    protected override async Task<CommandResult> RunAsync(ConnectFdgElementsCommand command, CancellationToken cancellationToken) =>
        await base.RunAsync(command.ConnectionId.Length > 0 ? command : command with { ConnectionId = ShortGuid.NewShortGuid().ToString() }, cancellationToken);

    /// <inheritdoc />
    protected override EditOutcome Apply(LineDocument document, FdgModel model, ConnectFdgElementsCommand command)
    {
        // Checked against the document as it is now, never against what the client believed.
        var refusal = FdgConnectVerdict.RefusalFor(model, command.RelationType, command.FromId, command.ToId);
        if (refusal is not null)
        {
            return EditOutcome.Refused(refusal);
        }

        if (model.IsTaken(command.ConnectionId))
        {
            return EditOutcome.Refused("That id is already used in this graph.");
        }

        var connection = new FdgConnection(
            command.ConnectionId,
            command.RelationType,
            command.FromId,
            command.ToId,
            Name: "",
            Description: "",
            Range: new LineRange(0, 0));

        return FdgWriter.Connect(document, model, connection);
    }
}
