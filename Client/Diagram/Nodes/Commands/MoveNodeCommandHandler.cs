namespace EtAlii.Adp.Client;
using Cn = CommandName;

public class MoveNodeCommandHandler : CommandHandler<NodeMoveCommand>
{
    private readonly ILogger _logger;

    public MoveNodeCommandHandler(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<MoveNodeCommandHandler>();
    }

    public override string CommandName => Cn.MoveNode;
    
    
    public static Command CreateCommand(DiagramContext context, NodeView nodeView)
    {
        var node = context.Diagram.Nodes.Single(n => n.Id == nodeView.Id);
        return new NodeMoveCommand
        {
            NodeId = nodeView.Id,
            NewPosition = nodeView.Position,
            OldPosition = node.Position
        };
    }
    
    protected override Task Do(NodeMoveCommand command, DiagramContext context)
    {
        _logger.LogInformation("Moving node {NodeIdentifier} from {FromPosition} to {ToPosition}", command.NodeId, command.OldPosition, command.NewPosition);

        var node = context.Diagram.Nodes.Single(n => n.Id == command.NodeId);
        node.Position = command.NewPosition;

        var view = context.View.Nodes.OfType<NodeView>().Single(nv => nv.Id == command.NodeId);
        view.Position = command.NewPosition;

        return Task.CompletedTask;
    }

    protected override Task Undo(NodeMoveCommand command, DiagramContext context)
    {
        _logger.LogInformation("Moving node {NodeIdentifier} from {FromPosition} to {ToPosition}", command.NodeId, command.NewPosition, command.OldPosition);
        var node = context.Diagram.Nodes.Single(n => n.Id == command.NodeId);
        node.Position = command.OldPosition;

        var view = context.View.Nodes.OfType<NodeView>().Single(nv => nv.Id == command.NodeId);
        view.Position = command.OldPosition;

        return Task.CompletedTask;
    }
}