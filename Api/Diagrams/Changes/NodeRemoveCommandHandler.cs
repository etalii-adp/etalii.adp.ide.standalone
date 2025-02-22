namespace EtAlii.Adp.Api;

public class NodeRemoveCommandHandler : CommandHandler<NodeRemoveCommand>
{
    private readonly NodeAddCommandHandler _nodeAddCommandHandler;

    public NodeRemoveCommandHandler(NodeAddCommandHandler nodeAddCommandHandler)
    {
        _nodeAddCommandHandler = nodeAddCommandHandler;
    }
    
    protected override async Task Do(NodeRemoveCommand command, AdpDbContext context)
    {
        await _nodeAddCommandHandler.Undo(context, command.DiagramId, command.OldNodeId);
    }
    
    protected override async Task Undo(NodeRemoveCommand command, AdpDbContext context)
    {
        await _nodeAddCommandHandler.Do(context, command.DiagramId, command.OldNodeId, command.OldNodePosition, command.OldNodeName);
    }
}