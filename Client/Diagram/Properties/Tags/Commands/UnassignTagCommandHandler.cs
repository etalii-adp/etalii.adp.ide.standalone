namespace EtAlii.Adp.Client;

public class UnassignTagCommandHandler : CommandHandler<TagUnassignCommand>
{
    private readonly AssignTagCommandHandler _assignTagCommandHandler;
    private readonly ILogger _logger;

    public UnassignTagCommandHandler(AssignTagCommandHandler assignTagCommandHandler, ILoggerFactory loggerFactory)
    {
        _assignTagCommandHandler = assignTagCommandHandler;
        _logger = loggerFactory.CreateLogger<UnassignTagCommandHandler>();
    }

    public static Command CreateCommand(DiagramContext _, TagGroupIdentifier tagGroupId, TagIdentifier tagId)
    {
        return new TagUnassignCommand
        {
            TagGroupId = tagGroupId,
            TagId = tagId,
        };
    }

    protected override Task Do(TagUnassignCommand command, DiagramContext context)
    {
        _logger.LogInformation("Unassigning tag {TagIdentifier} from {TagGroupIdentifier}", command.TagId, command.TagGroupId);

        return _assignTagCommandHandler.Undo(context, command.TagGroupId, command.TagId);
    }

    protected override Task Undo(TagUnassignCommand command, DiagramContext context)
    {
        _logger.LogInformation("Re-assigning tag {TagIdentifier} on {TagGroupIdentifier}", command.TagId, command.TagGroupId);

        return _assignTagCommandHandler.Do(context, command.TagGroupId, command.TagId);
    }
}