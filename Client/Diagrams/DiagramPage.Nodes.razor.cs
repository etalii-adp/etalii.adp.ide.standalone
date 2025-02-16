using System.Net.Http.Json;
using Blazor.Diagrams.Core.Events;
using Blazor.Diagrams.Core.Geometry;
using Blazor.Diagrams.Core.Models.Base;

namespace EtAlii.Adp.Client;

public partial class DiagramPage
{
    private async Task PopulateNodes()
    {
        var diagram = (await Client.GetFromJsonAsync<Diagram>(ApplicationApi.Diagram.Content.Request(_diagram.Id)))!;
        foreach (var n in diagram.Nodes)
        {
            var node = Node.Create(new Point(n.Position.X, n.Position.Y), n.Id);
            node.Moved += OnNodeMoved;
            _diagram.Nodes.Add(n);
            Diagram.Nodes.Add(node);
        }
    }

    private async void OnNodeMoved(MovableModel model)
    {
        try
        {
            var node = (Node)model;
            
            _logger.LogInformation("Moving node {NodeIdentifier} to {NodePosition}", node.Id, node.Position);

            var change = NodeMoveChange.Apply(_diagram, node.Id, new NodePosition { X = model.Position.X, Y = model.Position.Y });
            await ChangePusher.Enqueue(change);
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
            var point = Diagram.GetRelativeMousePoint(e.ClientX, e.ClientY);
            
            var node = Node.Create(point, NodeIdentifier.NewIdentifier());
            
            _logger.LogInformation("Double clicked diagram - adding node {NodeIdentifier} on {NodePosition}", node.Id, node.Position);

            Diagram.Nodes.Add(node);
        
            var change = NodeAddChange.Apply(_diagram, node.Position, node.Id);
            await ChangePusher.Enqueue(change);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Unable to handle {MethodName}", nameof(OnDiagramDoubleClicked));
        }
    }
}