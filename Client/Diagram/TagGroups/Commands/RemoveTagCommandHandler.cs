namespace EtAlii.Adp.Client;

public class RemoveTagCommandHandler : CommandHandler<TagRemoveCommand>
{
    private readonly AddTagCommandHandler _addTagCommandHandler;
    private readonly ILogger _logger;

    public RemoveTagCommandHandler(AddTagCommandHandler addTagCommandHandler, ILoggerFactory loggerFactory)
    {
        _addTagCommandHandler = addTagCommandHandler;
        _logger = loggerFactory.CreateLogger<RemoveTagCommandHandler>();
    }

    public static Command CreateCommand(DiagramContext _, TagGroupIdentifier tagGroupId, TagIdentifier oldTagId, string oldTagName)
    {
        return new TagRemoveCommand
        {
            TagGroupId = tagGroupId,
            OldTagId = oldTagId,
            OldTagName = oldTagName
        };
    }

    protected override Task Do(TagRemoveCommand command, DiagramContext context)
    {
        _logger.LogInformation("Removing tag {TagIdentifier} from {TagGroupIdentifier}", command.OldTagId, command.TagGroupId);

        return _addTagCommandHandler.Undo(context, command.TagGroupId, command.OldTagId);
    }

    protected override Task Undo(TagRemoveCommand command, DiagramContext context)
    {
        _logger.LogInformation("Re-adding tag {TagIdentifier} {TagName} on {TagGroupIdentifier}", command.OldTagId, command.OldTagName, command.TagGroupId);

        return _addTagCommandHandler.Do(context, command.TagGroupId, command.OldTagId, command.OldTagName);
    }
}