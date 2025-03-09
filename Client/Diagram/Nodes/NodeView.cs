using Blazor.Diagrams.Core.Geometry;
using Blazor.Diagrams.Core.Models;
using Blazor.Diagrams.Core.Models.Base;

namespace EtAlii.Adp.Client;

public class NodeView : NodeModel, IDisposable
{
    private readonly ILogger _logger;
    public string Name { get; set; }
    public new NodeIdentifier Id { get; }

    public Node Node { get; }
    
    public new NodePosition Position
    {
        get => new() { X = base.Position.X, Y = base.Position.Y };
        set => UpdatePositionSilently(value.X - base.Position.X, value.Y - base.Position.Y);
    }

    public DiagramContext Context { get; }

    public event Action EditRequested = null!;
    
    public NodeView(Node node, DiagramContext context, ILoggerFactory loggerFactory)
        : base(node.Id.ToString(), new Point(node.Position.X, node.Position.Y))
    {
        Node = node;
        Id = node.Id;
        Context = context;
        Moved += OnNodeMoved;
        _logger = loggerFactory.CreateLogger<NodeView>();
        Name = node.Name;
        AddPort(new PortView("Past", this, PortAlignment.Left));
        AddPort(new PortView("Future", this, PortAlignment.Right));
    }
    
    public void RequestNameEdit()
    {
        EditRequested.Invoke();
    }
    
    private void OnNodeMoved(MovableModel model)
    {
        try
        {
            var command = MoveNodeCommandHandler.CreateCommand(Context, (NodeView)model);
            Context.Commands.Handle(command);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Unable to handle {MethodName}", nameof(OnNodeMoved));
        }
    }

    public void Dispose()
    {
        Moved -= OnNodeMoved;
    }
}