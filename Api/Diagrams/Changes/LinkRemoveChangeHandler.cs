namespace EtAlii.Adp.Api;

public class LinkRemoveChangeHandler : ChangeHandler<LinkRemoveChange>
{
    private readonly LinkAddChangeHandler _linkAddChangeHandler;

    public LinkRemoveChangeHandler(LinkAddChangeHandler linkAddChangeHandler)
    {
        _linkAddChangeHandler = linkAddChangeHandler;
    }
    
    protected override async Task Do(LinkRemoveChange change, AdpDbContext context)
    {
        await _linkAddChangeHandler.Undo(context, change.OldLinkId);
    }
    
    protected override async Task Undo(LinkRemoveChange change, AdpDbContext context)
    {
        await _linkAddChangeHandler.Apply(context, change.DiagramId, change.OldLinkId, change.SourceNodeId, change.SourcePort, change.TargetNodeId, change.TargetPort);
    }
}