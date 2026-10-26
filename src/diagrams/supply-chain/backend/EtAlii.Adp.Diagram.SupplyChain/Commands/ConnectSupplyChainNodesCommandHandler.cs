using EtAlii.Adp.Documents;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.SupplyChain;

/// <summary>Adds a flow between two nodes, refused as the definition's connect constraints refuse drawing it.</summary>
public sealed class ConnectSupplyChainNodesCommandHandler(ISupplyChainDocumentStore documents) : SupplyChainEditHandler<ConnectSupplyChainNodesCommand>(documents)
{
    /// <inheritdoc />
    protected override async Task<CommandResult> RunAsync(ConnectSupplyChainNodesCommand command, CancellationToken cancellationToken) =>
        await base.RunAsync(command.FlowId.Length > 0 ? command : command with { FlowId = ShortGuid.NewShortGuid().ToString() }, cancellationToken);

    /// <inheritdoc />
    protected override EditOutcome Apply(LineDocument document, SupplyChainModel model, ConnectSupplyChainNodesCommand command) =>
        SupplyChainDefinition.Connect(document, model, command.From, command.To, command.FlowId);
}
