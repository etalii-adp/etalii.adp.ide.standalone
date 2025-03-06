namespace EtAlii.Adp.Client;

public class AssignTagCommandHandler : CommandHandler<TagAssignCommand>
{
    private readonly ILogger _logger;

    public AssignTagCommandHandler(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<AssignTagCommandHandler>();
    }

    public static Command CreateCommand(DiagramContext _, TagGroupIdentifier tagGroupId, TagIdentifier tagId)
    {
        return new TagAssignCommand
        {
            TagGroupId = tagGroupId,
            TagId = tagId,
        };
    }

    protected override Task Do(TagAssignCommand command, DiagramContext context)
    {
        return Do(context, command.TagGroupId, command.TagId);
    }
    
    public Task Do(DiagramContext context, TagGroupIdentifier tagGroupId, TagIdentifier tagId)
    {
        _logger.LogInformation("Assigning tag {TagIdentifier} on {TagGroupIdentifier}", tagId, tagGroupId);

        var tagGroup = context.Diagram.Nodes
            .SelectMany(n => n.TagGroups)
            .DistinctBy(g => g.Id)
            .Single(n => n.Id == tagGroupId);

        var tag = context.Diagram.Nodes
            .SelectMany(n => n.TagGroups)
            .SelectMany(g => g.Tags)
            .DistinctBy(t => t.Id)
            .Single(n => n.Id == tagId);

        tagGroup.Tags.Add(tag);
        tag.TagGroups.Add(tagGroup);

        return Task.CompletedTask;
    }

    protected override Task Undo(TagAssignCommand command, DiagramContext context)
    {
        return Undo(context, command.TagGroupId, command.TagId);
    }
    
    public Task Undo(DiagramContext context, TagGroupIdentifier tagGroupId, TagIdentifier tagId)
    {
        _logger.LogInformation("Unassigning tag {TagIdentifier} from {TagGroupIdentifier}", tagId, tagGroupId);

        var tagGroup = context.Diagram.Nodes
            .SelectMany(n => n.TagGroups)
            .DistinctBy(g => g.Id)
            .Single(n => n.Id == tagGroupId);

        var tag = tagGroup.Tags.Single(g => g.Id == tagId);
        tagGroup.Tags.Remove(tag);
        tag.TagGroups.Remove(tagGroup);
        
        return Task.CompletedTask;
    }
}