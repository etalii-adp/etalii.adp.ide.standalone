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

    public void Deinitialize()
    {
        _context.View.PointerDoubleClick -= OnDiagramDoubleClicked;
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

            var nodeIdentifier = NodeIdentifier.NewIdentifier();
            var typeTagGroupIdentifier = TagGroupIdentifier.NewIdentifier();
            var typeTagIdentifier = TagIdentifier.NewIdentifier();
            var statusTagGroupIdentifier = TagGroupIdentifier.NewIdentifier();
            var statusTagIdentifier = TagIdentifier.NewIdentifier();
            var layerTagGroupIdentifier = TagGroupIdentifier.NewIdentifier();
            var layerTagIdentifier = TagIdentifier.NewIdentifier();
            Command[] commands =
            [
                AddNodeCommandHandler.CreateCommand(_context, position, nodeIdentifier, "New element"),
                AddTagGroupCommandHandler.CreateCommand(_context, nodeIdentifier, typeTagGroupIdentifier, "Type", TagGroupMode.Single),
                AddTagCommandHandler.CreateCommand(_context, typeTagGroupIdentifier, typeTagIdentifier, "Default"),
                AddTagGroupCommandHandler.CreateCommand(_context, nodeIdentifier, statusTagGroupIdentifier, "Status", TagGroupMode.Single),
                AddTagCommandHandler.CreateCommand(_context, statusTagGroupIdentifier, statusTagIdentifier, "None"),
                AddTagGroupCommandHandler.CreateCommand(_context, nodeIdentifier, layerTagGroupIdentifier, "Layer", TagGroupMode.Multiple),
                AddTagCommandHandler.CreateCommand(_context, layerTagGroupIdentifier, layerTagIdentifier, "All"),
            ];
            _context.Commands.Handle(commands);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Unable to handle {MethodName}", nameof(OnDiagramDoubleClicked));
        }
    }
}