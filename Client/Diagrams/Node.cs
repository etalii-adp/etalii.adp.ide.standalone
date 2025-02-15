using Blazor.Diagrams.Core.Geometry;
using Blazor.Diagrams.Core.Models;

namespace EtAlii.Adp.Client;

public class Node : NodeModel
{
    public new NodeIdentifier Id { get; }
    public new NodePosition Position => new NodePosition { X = base.Position.X, Y = base.Position.Y };

    private Node(NodeIdentifier id, Point position)
        : base(id.ToString(), position)
    {
        Id = id;
    }

    public static Node Create(Point position, NodeIdentifier id)
    {
        // TODO: We currently do left-top positioning, but need to do a small correction to place the node from its the center. 
        var node = new Node(id, position);
        node.AddPort();//PortAlignment.Bottom);
        node.AddPort(PortAlignment.Top);
        node.AddPort(PortAlignment.Left);
        node.AddPort(PortAlignment.Right);
        return node;
    }
}