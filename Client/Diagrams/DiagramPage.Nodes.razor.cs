using Blazor.Diagrams.Core.Events;
using Blazor.Diagrams.Core.Geometry;
using Blazor.Diagrams.Core.Models;
using Blazor.Diagrams.Core.Models.Base;

namespace EtAlii.Adp.Client;

public partial class DiagramPage
{
    
    private NodeModel NewNode(double x, double y)
    {
        var node = new NodeModel(new Point(x, y));
        node.AddPort(PortAlignment.Bottom);
        node.AddPort(PortAlignment.Top);
        node.AddPort(PortAlignment.Left);
        node.AddPort(PortAlignment.Right);
        return node;
    }

    private void OnDiagramDoubleClicked(Model? model, PointerEventArgs e)
    {
        var node = NewNode(e.ClientX, e.ClientY);
        Diagram.Nodes.Add(node);
    }
}