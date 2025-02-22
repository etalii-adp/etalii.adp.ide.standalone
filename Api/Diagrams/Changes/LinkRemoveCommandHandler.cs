namespace EtAlii.Adp.Api;

public class LinkRemoveCommandHandler : CommandHandler<LinkRemoveCommand>
{
    private readonly LinkAddCommandHandler _linkAddCommandHandler;

    public LinkRemoveCommandHandler(LinkAddCommandHandler linkAddCommandHandler)
    {
        _linkAddCommandHandler = linkAddCommandHandler;
    }
    
    protected override async Task Do(LinkRemoveCommand command, AdpDbContext context)
    {
        await _linkAddCommandHandler.Undo(context, command.OldLinkId);
    }
    
    protected override async Task Undo(LinkRemoveCommand command, AdpDbContext context)
    {
        await _linkAddCommandHandler.Do(context, command.DiagramId, command.OldLinkId, command.SourceNodeId, command.SourcePort, command.TargetNodeId, command.TargetPort);
    }
}