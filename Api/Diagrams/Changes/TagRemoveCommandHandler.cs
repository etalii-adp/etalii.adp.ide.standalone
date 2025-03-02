namespace EtAlii.Adp.Api;

public class TagRemoveCommandHandler : CommandHandler<TagRemoveCommand>
{
    private readonly TagAddCommandHandler _tagAddCommandHandler;

    public TagRemoveCommandHandler(TagAddCommandHandler tagAddCommandHandler)
    {
        _tagAddCommandHandler = tagAddCommandHandler;
    }

    protected override Task Do(TagRemoveCommand command, AdpDbContext context)
    {
        return _tagAddCommandHandler.Undo(context, command.TagGroupId, command.OldTagId);
    }

    protected override Task Undo(TagRemoveCommand command, AdpDbContext context)
    {
        return _tagAddCommandHandler.Do(context, command.TagGroupId, command.OldTagId, command.OldTagName);
    }
}