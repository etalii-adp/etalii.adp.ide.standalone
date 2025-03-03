namespace EtAlii.Adp.Client;

public class RemoveTagGroupCommandHandler : CommandHandler<TagGroupRemoveCommand>
{
    private readonly AddTagGroupCommandHandler _addTagGroupCommandHandler;
    private readonly ILogger _logger;

    public RemoveTagGroupCommandHandler(AddTagGroupCommandHandler addTagGroupCommandHandler, ILoggerFactory loggerFactory)
    {
        _addTagGroupCommandHandler = addTagGroupCommandHandler;
        _logger = loggerFactory.CreateLogger<RemoveTagGroupCommandHandler>();
    }

    public static Command Create(DiagramContext context, NodeIdentifier nodeId, TagGroupIdentifier oldTagGroupId, string oldTagGroupName)
    {
        return new TagGroupRemoveCommand
        {
            NodeId = nodeId,
            OldTagGroupId = oldTagGroupId,
            OldTagGroupName = oldTagGroupName
        };
    }

    protected override Task Do(TagGroupRemoveCommand command, DiagramContext context)
    {
        _logger.LogInformation("Removing tag group {TagGroupIdentifier} from {NodeIdentifier}", command.OldTagGroupId, command.NodeId);

        return _addTagGroupCommandHandler.Undo(context, command.NodeId, command.OldTagGroupId);
    }

    protected override Task Undo(TagGroupRemoveCommand command, DiagramContext context)
    {
        _logger.LogInformation("Re-adding tag group {TagGroupIdentifier} {TagGroupName} on {NodeIdentifier}", command.OldTagGroupId, command.OldTagGroupName, command.NodeId);

        return _addTagGroupCommandHandler.Do(context, command.NodeId, command.OldTagGroupId, command.OldTagGroupName);
    }
}