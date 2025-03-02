namespace EtAlii.Adp.Client;

public class AddNodeCommandHandler : CommandHandler<NodeAddCommand>
{
    private readonly ILogger _logger;

    public AddNodeCommandHandler(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<AddNodeCommandHandler>();
    }

    public static Command CreateCommand(DiagramContext context, NodePosition position, NodeIdentifier nodeId, string name)
    {
        return new NodeAddCommand
        {
            DiagramId = context.Diagram.Id, 
            NewNodeId = nodeId,
            NewPosition = position,
            NewName = name,
        };
    }

    protected override Task Do(NodeAddCommand command, DiagramContext context)
    {
        return Do(context, command.NewNodeId, command.NewPosition, command.NewName);
    }
    
    public Task Do(DiagramContext context, NodeIdentifier nodeId, NodePosition nodePosition, string nodeName)
    {
        _logger.LogInformation("Adding node {NodeIdentifier} on {NodePosition}", nodeId, nodePosition);

        var node = context.NodeFactory.Create(context.Diagram, nodeId, nodePosition, nodeName);
        context.Diagram.Nodes.Add(node);

        var view = NodeView.Create(context, node);
        context.View.Nodes.Add(view);

        return Task.CompletedTask;
    }

    protected override Task Undo(NodeAddCommand command, DiagramContext context)
    {
        return Undo(context, command.NewNodeId, command.NewPosition);
    }
    
    public Task Undo(DiagramContext context, NodeIdentifier nodeId, NodePosition nodePosition)
    {
        _logger.LogInformation("Removing node {NodeIdentifier} from {NodePosition}", nodeId, nodePosition);

        var node = context.Diagram.Nodes.Single(n => n.Id == nodeId);
        context.Diagram.Nodes.Remove(node);
        
        var view = context.View.Nodes.OfType<NodeView>().Single(v => v.Id == nodeId);
        context.View.Nodes.Remove(view);
        
        return Task.CompletedTask;
    }
}