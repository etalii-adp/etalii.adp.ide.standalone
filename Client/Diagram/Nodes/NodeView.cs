using Blazor.Diagrams.Core.Geometry;
using Blazor.Diagrams.Core.Models;

namespace EtAlii.Adp.Client;

public class NodeView : NodeModel
{
    public string Name { get; set; } = string.Empty;
    public new NodeIdentifier Id { get; }

    public new NodePosition Position
    {
        get => new() { X = base.Position.X, Y = base.Position.Y };
        set => UpdatePositionSilently(value.X - base.Position.X, value.Y - base.Position.Y);
    }

    public DiagramContext Context { get; }

    public event Action EditRequested = null!;
    
    private NodeView(NodeIdentifier id, NodePosition position, DiagramContext context)
        : base(id.ToString(), new Point(position.X, position.Y))
    {
        Id = id;
        Context = context;
    }

    public static NodeView Create(DiagramContext context, Node node)
    {
        var view = new NodeView(node.Id, node.Position, context)
        {
            Name = node.Name,
        };
        view.AddPort(new PortView("Past", view, PortAlignment.Left));
        view.AddPort(new PortView("Future", view, PortAlignment.Right));
        return view;
    }

    public void RequestNameEdit()
    {
        EditRequested.Invoke();
    }
}