using Blazor.Diagrams.Core.Events;
using Blazor.Diagrams.Core.Models.Base;

namespace EtAlii.Adp.Client;

public class NodeManager
{
    private readonly DiagramView _view;
    private readonly Diagram _diagram;
    private readonly ChangePusher _changePusher;
    private readonly ILogger _logger;

    public NodeManager(
        DiagramView view, Diagram diagram,
        ChangePusher changePusher, ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<NodeManager>();
        _view = view;
        _diagram = diagram;
        _changePusher = changePusher;
    }

    public async Task Initialize()
    {
        _logger.LogInformation("Initializing node management");

        foreach (var n in _diagram.Nodes)
        {
            var nodeView = NodeView.Create(this, n);
            nodeView.Moved += OnNodeMoved;
            _view.Nodes.Add(nodeView);
        }
        _view.PointerDoubleClick += OnDiagramDoubleClicked;

        await Task.CompletedTask;
    }
    


    private async void OnNodeMoved(MovableModel model)
    {
        try
        {
            var nodeView = (NodeView)model;
            
            _logger.LogInformation("Moving node {NodeIdentifier} to {NodePosition}", nodeView.Id, nodeView.Position);

            var change = NodeMoveChange.Apply(_diagram, nodeView.Id, new NodePosition { X = model.Position.X, Y = model.Position.Y });
            await _changePusher.Enqueue(change);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Unable to handle {MethodName}", nameof(OnDiagramDoubleClicked));
        }
    }
    
    private async void OnDiagramDoubleClicked(Model? model, PointerEventArgs e)
    {
        try
        {
            if (model is not null) return;
            
            var point = _view.GetRelativeMousePoint(e.ClientX, e.ClientY);
            
            var nodeView = NodeView.Create(this, point, NodeIdentifier.NewIdentifier());
            
            _logger.LogInformation("Double clicked diagram - adding node {NodeIdentifier} on {NodePosition}", nodeView.Id, nodeView.Position);

            _view.Nodes.Add(nodeView);
        
            var change = NodeAddChange.Apply(_diagram, nodeView.Position, nodeView.Id, nodeView.Name);
            await _changePusher.Enqueue(change);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Unable to handle {MethodName}", nameof(OnDiagramDoubleClicked));
        }
    }

    public async Task RenameNode(NodeView nodeView, string oldName, string newName)
    {
        try
        {
            _logger.LogInformation("Renaming node {NodeIdentifier} from {OldName} to {NewName}", nodeView.Id, oldName, newName);

            var change = NodeRenameChange.Apply(_diagram, nodeView.Id, oldName, newName);
            await _changePusher.Enqueue(change);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Unable to handle {MethodName}", nameof(RenameNode));
        }
    }
}