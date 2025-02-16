using Blazor.Diagrams.Core.Geometry;
using Blazor.Diagrams.Core.Models;

namespace EtAlii.Adp.Client;

public class NodeView : NodeModel
{
    public new NodeIdentifier Id { get; }
    public new NodePosition Position => new NodePosition { X = base.Position.X, Y = base.Position.Y };

    private NodeView(NodeIdentifier id, Point position)
        : base(id.ToString(), position)
    {
        Id = id;
    }

    public static NodeView Create(Point position, NodeIdentifier id)
    {
        var node = new NodeView(id, position);
        node.AddPort(new PortView("Past", node, PortAlignment.Left));
        node.AddPort(new PortView("Future", node, PortAlignment.Right));
        return node;
    }
}