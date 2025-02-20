namespace EtAlii.Adp.Api;

public class NodeRemoveChangeHandler : ChangeHandler<NodeRemoveChange>
{
    private readonly NodeAddChangeHandler _nodeAddChangeHandler;

    public NodeRemoveChangeHandler(NodeAddChangeHandler nodeAddChangeHandler)
    {
        _nodeAddChangeHandler = nodeAddChangeHandler;
    }
    
    protected override async Task Apply(NodeRemoveChange change, AdpDbContext context)
    {
        await _nodeAddChangeHandler.Undo(context, change.DiagramId, change.OldNodeId);
    }
    
    protected override async Task Undo(NodeRemoveChange change, AdpDbContext context)
    {
        await _nodeAddChangeHandler.Apply(context, change.DiagramId, change.OldNodeId, change.OldNodePosition);
    }
}