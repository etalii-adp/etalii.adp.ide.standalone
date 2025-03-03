namespace EtAlii.Adp.Client;

public class AddTagGroupCommandHandler : CommandHandler<TagGroupAddCommand>
{
    private readonly ILogger _logger;

    public AddTagGroupCommandHandler(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<AddTagGroupCommandHandler>();
    }

    public static Command CreateCommand(DiagramContext _, NodeIdentifier nodeId, TagGroupIdentifier tagGroupId, string name)
    {
        return new TagGroupAddCommand
        {
            NodeId = nodeId,
            NewTagGroupId = tagGroupId,
            NewTagGroupName = name
        };
    }

    protected override Task Do(TagGroupAddCommand command, DiagramContext context)
    {
        return Do(context, command.NodeId, command.NewTagGroupId, command.NewTagGroupName);
    }
    
    public Task Do(DiagramContext context, NodeIdentifier nodeId, TagGroupIdentifier tagGroupId, string tagGroupName)
    {
        _logger.LogInformation("Adding tag group {TagGroupIdentifier} {TagGroupName} on {NodeIdentifier}", tagGroupId, tagGroupName, nodeId);

        var node = context.Diagram.Nodes.Single(n => n.Id == nodeId);

        var tagGroup = new TagGroup
        {
            Id = tagGroupId,
            Name = tagGroupName
        };
        
        node.TagGroups.Add(tagGroup);

        return Task.CompletedTask;
    }

    protected override Task Undo(TagGroupAddCommand command, DiagramContext context)
    {
        return Undo(context, command.NodeId, command.NewTagGroupId);
    }
    
    public Task Undo(DiagramContext context, NodeIdentifier nodeId, TagGroupIdentifier tagGroupId)
    {
        _logger.LogInformation("Removing tag group {TagGroupIdentifier} from {NodeIdentifier}", tagGroupId, nodeId);

        var node = context.Diagram.Nodes.Single(n => n.Id == nodeId);

        var tagGroup = node.TagGroups.Single(g => g.Id == tagGroupId);
        node.TagGroups.Remove(tagGroup);

        // var view = context.View.Nodes.OfType<NodeView>().Single(v => v.Id == nodeId);
        // context.View.Nodes.Remove(view);
        
        return Task.CompletedTask;
    }
}