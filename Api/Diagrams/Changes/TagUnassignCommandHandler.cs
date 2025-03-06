namespace EtAlii.Adp.Api;

public class TagUnassignCommandHandler : CommandHandler<TagUnassignCommand>
{
    private readonly TagAssignCommandHandler _tagAssignCommandHandler;

    public TagUnassignCommandHandler(TagAssignCommandHandler tagAssignCommandHandler)
    {
        _tagAssignCommandHandler = tagAssignCommandHandler;
    }

    protected override Task Do(TagUnassignCommand command, AdpDbContext context)
    {
        return _tagAssignCommandHandler.Undo(context, command.TagGroupId, command.TagId);
    }

    protected override Task Undo(TagUnassignCommand command, AdpDbContext context)
    {
        return _tagAssignCommandHandler.Do(context, command.TagGroupId, command.TagId);
    }
}