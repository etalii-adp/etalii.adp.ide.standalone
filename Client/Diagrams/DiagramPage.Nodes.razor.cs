using System.Net.Http.Json;
using Blazor.Diagrams.Core.Events;
using Blazor.Diagrams.Core.Geometry;
using Blazor.Diagrams.Core.Models;
using Blazor.Diagrams.Core.Models.Base;

namespace EtAlii.Adp.Client;

public partial class DiagramPage
{
    private async Task PopulateNodes()
    {
        var diagram = (await Client.GetFromJsonAsync<Diagram>(ApplicationApi.Diagram.Content.Request(_diagram.Id)))!;
        foreach (var n in diagram.Nodes)
        {
            var node = NewNode(new Point(n.Position.X, n.Position.Y));
            Diagram.Nodes.Add(node);
        }
    }
    
    private NodeModel NewNode(Point position)
    {
        // TODO: We currently do left-top positioning, but need to do a small correction to place the node from its the center. 
        var node = new NodeModel(position);
        node.AddPort();//PortAlignment.Bottom);
        node.AddPort(PortAlignment.Top);
        node.AddPort(PortAlignment.Left);
        node.AddPort(PortAlignment.Right);
        return node;
    }

    private async void OnDiagramDoubleClicked(Model? model, PointerEventArgs e)
    {
        try
        {
            var point = Diagram.GetRelativeMousePoint(e.ClientX, e.ClientY);
            var node = NewNode(point);
            Diagram.Nodes.Add(node);
        
            var change = NodeAddChange.Apply(_diagram, new NodePosition { X = point.X, Y = point.Y});
            await ChangePusher.Enqueue(change);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Unable to handle {MethodName}", nameof(OnDiagramDoubleClicked));
        }
    }
}