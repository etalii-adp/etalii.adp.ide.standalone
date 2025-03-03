namespace EtAlii.Adp.Client;

public class AddTagCommandHandler : CommandHandler<TagAddCommand>
{
    private readonly ILogger _logger;

    public AddTagCommandHandler(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<AddTagCommandHandler>();
    }

    public static Command CreateCommand(DiagramContext _, TagGroupIdentifier tagGroupId, TagIdentifier tagId, string name)
    {
        return new TagAddCommand
        {
            TagGroupId = tagGroupId,
            NewTagId = tagId,
            NewTagName = name
        };
    }

    protected override Task Do(TagAddCommand command, DiagramContext context)
    {
        return Do(context, command.TagGroupId, command.NewTagId, command.NewTagName);
    }
    
    public Task Do(DiagramContext context, TagGroupIdentifier tagGroupId, TagIdentifier tagId, string tagName)
    {
        _logger.LogInformation("Adding tag {TagIdentifier} {TagName} on {TagGroupIdentifier}", tagId, tagName, tagGroupId);

        var tagGroup = context.Diagram.Nodes
            .SelectMany(n => n.TagGroups)
            .Single(n => n.Id == tagGroupId);

        var tag = new Tag
        {
            Id = tagId,
            Name = tagName
        };
        
        tagGroup.Tags.Add(tag);

        return Task.CompletedTask;
    }

    protected override Task Undo(TagAddCommand command, DiagramContext context)
    {
        return Undo(context, command.TagGroupId, command.NewTagId);
    }
    
    public Task Undo(DiagramContext context, TagGroupIdentifier tagGroupId, TagIdentifier tagId)
    {
        _logger.LogInformation("Removing tag {TagIdentifier} from {TagGroupIdentifier}", tagId, tagGroupId);

        var node = context.Diagram.Nodes
            .SelectMany(n => n.TagGroups)
            .Single(n => n.Id == tagGroupId);

        var tag = node.Tags.Single(g => g.Id == tagId);
        node.Tags.Remove(tag);

        // var view = context.View.Nodes.OfType<NodeView>().Single(v => v.Id == nodeId);
        // context.View.Nodes.Remove(view);
        
        return Task.CompletedTask;
    }
}