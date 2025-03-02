namespace EtAlii.Adp.Client;

public class RenameTagGroupCommandHandler : CommandHandler<TagGroupRenameCommand>
{
    private readonly ILogger _logger;

    public RenameTagGroupCommandHandler(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<RenameNodeCommandHandler>();
    }


    public static Command CreateCommand(TagGroupIdentifier tagGroupId, string oldName, string newName)
    {
        return new TagGroupRenameCommand
        {
            TagGroupId = tagGroupId,
            NewName = newName,
            OldName = oldName
        };
    }
    protected override Task Do(TagGroupRenameCommand command, DiagramContext context)
    {
        _logger.LogInformation("Renaming tag group {TagGroupIdentifier} from {FromName} to {ToName}", command.TagGroupId, command.OldName, command.NewName);

        var tagGroup = context.Diagram.Nodes
            .SelectMany(n => n.TagGroups)
            .Single(g => g.Id == command.TagGroupId);
        tagGroup.Name = command.NewName;
        
        // var view = context.View.Nodes.OfType<NodeView>().Single(v => v.Id == command.NodeId);
        // view.Name = command.NewName;
        // view.Refresh();
        
        return Task.CompletedTask;
    }

    protected override Task Undo(TagGroupRenameCommand command, DiagramContext context)
    {
        _logger.LogInformation("Renaming tag group {TagGroupIdentifier} from {FromName} to {ToName}", command.TagGroupId, command.NewName, command.OldName);

        var tagGroup = context.Diagram.Nodes
            .SelectMany(n => n.TagGroups)
            .Single(g => g.Id == command.TagGroupId);
        tagGroup.Name = command.OldName;
        
        // var view = context.View.Nodes.OfType<NodeView>().Single(v => v.Id == command.NodeId);
        // view.Name = command.OldName;
        // view.Refresh();

        return Task.CompletedTask;
    }
}