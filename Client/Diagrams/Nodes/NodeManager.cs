using System.Net.Http.Json;
using Blazor.Diagrams.Core.Events;
using Blazor.Diagrams.Core.Geometry;
using Blazor.Diagrams.Core.Models.Base;

namespace EtAlii.Adp.Client;

public class NodeManager
{
    private readonly DiagramView _view;
    private readonly Diagram _diagram;
    private readonly HttpClient _client;
    private readonly ChangePusher _changePusher;
    private readonly ILogger _logger;

    public NodeManager(
        DiagramView view, Diagram diagram, HttpClient client,
        ChangePusher changePusher, ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<NodeManager>();
        _view = view;
        _diagram = diagram;
        _client = client;
        _changePusher = changePusher;
    }

    public async Task Initialize()
    {
        _logger.LogInformation("Initializing node management");

        var diagram = (await _client.GetFromJsonAsync<Diagram>(ApplicationApi.Diagram.Content.Request(_diagram.Id)))!;
        foreach (var n in diagram.Nodes)
        {
            var node = Node.Create(new Point(n.Position.X, n.Position.Y), n.Id);
            node.Moved += OnNodeMoved;
            _diagram.Nodes.Add(n);
            _view.Nodes.Add(node);
        }
        _view.PointerDoubleClick += OnDiagramDoubleClicked;
    }
    


    private async void OnNodeMoved(MovableModel model)
    {
        try
        {
            var node = (Node)model;
            
            _logger.LogInformation("Moving node {NodeIdentifier} to {NodePosition}", node.Id, node.Position);

            var change = NodeMoveChange.Apply(_diagram, node.Id, new NodePosition { X = model.Position.X, Y = model.Position.Y });
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
            var point = _view.GetRelativeMousePoint(e.ClientX, e.ClientY);
            
            var node = Node.Create(point, NodeIdentifier.NewIdentifier());
            
            _logger.LogInformation("Double clicked diagram - adding node {NodeIdentifier} on {NodePosition}", node.Id, node.Position);

            _view.Nodes.Add(node);
        
            var change = NodeAddChange.Apply(_diagram, node.Position, node.Id);
            await _changePusher.Enqueue(change);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Unable to handle {MethodName}", nameof(OnDiagramDoubleClicked));
        }
    }
}