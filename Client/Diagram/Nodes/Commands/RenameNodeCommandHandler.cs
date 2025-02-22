namespace EtAlii.Adp.Client;
using Cn = CommandName;

public class RenameNodeCommandHandler : CommandHandler<NodeRenameCommand>
{
    private readonly ILogger _logger;

    public RenameNodeCommandHandler(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<RenameNodeCommandHandler>();
    }

    public override string CommandName => Cn.RenameNode;
    

    public static Command CreateCommand(NodeIdentifier nodeId, string oldName, string newName)
    {
        return new NodeRenameCommand
        {
            NodeId = nodeId,
            NewName = newName,
            OldName = oldName
        };
    }
    protected override Task Do(NodeRenameCommand command, DiagramContext context)
    {
        _logger.LogInformation("Renaming node {NodeIdentifier} from {FromName} to {ToName}", command.NodeId, command.OldName, command.NewName);

        var node = context.Diagram.Nodes.Single(n => n.Id == command.NodeId);
        node.Name = command.NewName;
        
        var view = context.View.Nodes.OfType<NodeView>().Single(v => v.Id == command.NodeId);
        view.Name = command.NewName;
        view.Refresh();
        
        return Task.CompletedTask;
    }

    protected override Task Undo(NodeRenameCommand command, DiagramContext context)
    {
        _logger.LogInformation("Renaming node {NodeIdentifier} from {FromName} to {ToName}", command.NodeId, command.NewName, command.OldName);

        var node = context.Diagram.Nodes.Single(n => n.Id == command.NodeId);
        node.Name = command.OldName;
        
        var view = context.View.Nodes.OfType<NodeView>().Single(v => v.Id == command.NodeId);
        view.Name = command.OldName;
        view.Refresh();

        return Task.CompletedTask;
    }
}