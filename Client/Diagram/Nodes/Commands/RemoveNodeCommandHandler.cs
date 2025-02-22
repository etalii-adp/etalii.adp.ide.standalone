namespace EtAlii.Adp.Client;

public class RemoveNodeCommandHandler : CommandHandler<NodeRemoveCommand>
{
    private readonly AddNodeCommandHandler _addNodeCommandHandler;
    private readonly ILogger _logger;

    public RemoveNodeCommandHandler(AddNodeCommandHandler addNodeCommandHandler, ILoggerFactory loggerFactory)
    {
        _addNodeCommandHandler = addNodeCommandHandler;
        _logger = loggerFactory.CreateLogger<RemoveNodeCommandHandler>();
    }

    public static Command Create(DiagramContext context, NodeIdentifier oldNodeId, NodePosition oldNodePosition, string oldNodeName)
    {
        return new NodeRemoveCommand
        {
            DiagramId = context.Diagram.Id, 
            OldNodeId = oldNodeId,
            OldNodePosition = oldNodePosition,
            OldNodeName = oldNodeName,
        };
    }

    protected override Task Do(NodeRemoveCommand command, DiagramContext context)
    {
        _logger.LogInformation("Removing node {NodeIdentifier} on {NodePosition}", command.OldNodeId, command.OldNodePosition);

        return _addNodeCommandHandler.Undo(context, command.OldNodeId, command.OldNodePosition);
    }

    protected override Task Undo(NodeRemoveCommand command, DiagramContext context)
    {
        _logger.LogInformation("Re-adding node {NodeIdentifier} on {NodePosition}", command.OldNodeId, command.OldNodePosition);

        return _addNodeCommandHandler.Do(context, command.OldNodeId, command.OldNodePosition, command.OldNodeName);
    }
}