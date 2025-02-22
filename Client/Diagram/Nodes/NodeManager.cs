using Blazor.Diagrams.Core.Events;
using Blazor.Diagrams.Core.Models.Base;

namespace EtAlii.Adp.Client;

public class NodeManager
{
    private DiagramContext _context = null!;
    private readonly ILogger _logger;

    public NodeManager(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<NodeManager>();
    }
    
    public void Initialize(DiagramContext context)
    {
        _context = context;
        _logger.LogInformation("Initializing node management");

        foreach (var n in context.Diagram.Nodes)
        {
            var nodeView = NodeView.Create(context, n);
            nodeView.Moved += OnNodeMoved;
            context.View.Nodes.Add(nodeView);
        }
        context.View.PointerDoubleClick += OnDiagramDoubleClicked;
    }

    
    private void OnNodeMoved(MovableModel model)
    {
        try
        {
            var command = MoveNodeCommandHandler.CreateCommand(_context, (NodeView)model);
            _context.Commands.Handle(command);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Unable to handle {MethodName}", nameof(OnDiagramDoubleClicked));
        }
    }
    
    private void OnDiagramDoubleClicked(Model? model, PointerEventArgs e)
    {
        try
        {
            if (model is not null) return;
            
            var point = _context.View.GetRelativeMousePoint(e.ClientX, e.ClientY);
            var position = new NodePosition { X = point.X, Y = point.Y };
            
            _logger.LogInformation("Double clicked diagram {NodePosition}", position);

            var command = AddNodeCommandHandler.CreateCommand(_context, position, NodeIdentifier.NewIdentifier(), "New element");        
            _context.Commands.Handle(command);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Unable to handle {MethodName}", nameof(OnDiagramDoubleClicked));
        }
    }
}