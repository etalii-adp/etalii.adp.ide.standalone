using Blazor.Diagrams.Core.Events;
using Blazor.Diagrams.Core.Geometry;
using Blazor.Diagrams.Core.Models;
using Blazor.Diagrams.Core.Models.Base;

namespace EtAlii.Adp.Client;

public partial class DiagramPage
{
    
    private NodeModel NewNode(Point position)
    {
        // TODO: We currently do left-top positioning, but need to do a small correction to place the node from its the center. 
        var node = new NodeModel(position);
        node.AddPort(PortAlignment.Bottom);
        node.AddPort(PortAlignment.Top);
        node.AddPort(PortAlignment.Left);
        node.AddPort(PortAlignment.Right);
        return node;
    }

    private void OnDiagramDoubleClicked(Model? model, PointerEventArgs e)
    {
        var point = Diagram.GetRelativeMousePoint(e.ClientX, e.ClientY);
        var node = NewNode(point);
        Diagram.Nodes.Add(node);
        //Diagram
    }
}