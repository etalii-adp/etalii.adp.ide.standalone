namespace EtAlii.Adp.Api;

public class TagGroupRemoveCommandHandler : CommandHandler<TagGroupRemoveCommand>
{
    private readonly TagGroupAddCommandHandler _tagGroupAddCommandHandler;

    public TagGroupRemoveCommandHandler(TagGroupAddCommandHandler tagGroupAddCommandHandler)
    {
        _tagGroupAddCommandHandler = tagGroupAddCommandHandler;
    }

    protected override Task Do(TagGroupRemoveCommand command, AdpDbContext context)
    {
        return _tagGroupAddCommandHandler.Undo(context, command.NodeId, command.OldTagGroupId);
    }

    protected override Task Undo(TagGroupRemoveCommand command, AdpDbContext context)
    {
        return _tagGroupAddCommandHandler.Do(context, command.NodeId, command.OldTagGroupId, command.OldTagGroupName, command.OldTagGroupMode);
    }
}