using Blazor.Diagrams.Core.Geometry;
using Blazor.Diagrams.Core.Models;

namespace EtAlii.Adp.Client;

public class NodeView : NodeModel
{
    public string Name { get; set; } = string.Empty;
    public new NodeIdentifier Id { get; }
    public new NodePosition Position => new() { X = base.Position.X, Y = base.Position.Y };

    public NodeManager NodeManager { get; }

    public event Action EditRequested = null!;
    
    private NodeView(NodeIdentifier id, Point position, NodeManager nodeManager)
        : base(id.ToString(), position)
    {
        Id = id;
        NodeManager = nodeManager;
    }

    public static NodeView Create(NodeManager nodeManager, Node node)
    {
        var position = new Point(node.Position.X, node.Position.Y);
        var view = Create(nodeManager, position, node.Id);
        view.Name = node.Name;
        return view;
    }

    public static NodeView Create(NodeManager nodeManager, Point position, NodeIdentifier id)
    {
        var node = new NodeView(id, position, nodeManager)
        {
            Name = "New element",
        };
        node.AddPort(new PortView("Past", node, PortAlignment.Left));
        node.AddPort(new PortView("Future", node, PortAlignment.Right));
        return node;
    }

    public void RequestNameEdit()
    {
        EditRequested.Invoke();
    }
}