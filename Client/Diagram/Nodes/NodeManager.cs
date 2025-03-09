using Blazor.Diagrams.Core.Events;
using Blazor.Diagrams.Core.Models.Base;

namespace EtAlii.Adp.Client;

public class NodeManager
{
    private DiagramContext _context = null!;
    private readonly ILogger _logger;
    private readonly ILoggerFactory _loggerFactory;
    
    public NodeManager(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<NodeManager>();
    }
    
    public void Initialize(DiagramContext context)
    {
        _context = context;
        _logger.LogInformation("Initializing node management");

        foreach (var n in context.Diagram.Nodes)
        {
            var nodeView = new NodeView(n, context, _loggerFactory);
            context.View.Nodes.Add(nodeView);
        }
        context.View.PointerDoubleClick += OnDiagramDoubleClicked;
    }

    public void Deinitialize()
    {
        _context.View.PointerDoubleClick -= OnDiagramDoubleClicked;
    }
    

    private (TagGroupIdentifier GroupIdentifier, TagIdentifier TagIdentifier, bool AssignTypeTag) CreateTagAndGroup(
        string groupName,
        string tagName)
    {
        var tagGroupId = TagGroupIdentifier.NewIdentifier();
        var tag = _context.Diagram.Nodes
            .SelectMany(n => n.TagGroups)
            .DistinctBy(g => g.Id)
            .Where(g => g.Name == groupName)
            .SelectMany(g => g.Tags)
            .DistinctBy(t => t.Id)
            .SingleOrDefault(t => t.Name == tagName);
        var assignTag = tag != null;
        var tagId = tag?.Id ?? TagIdentifier.NewIdentifier();

        return (tagGroupId, tagId, assignTag);
    }
    
    private void OnDiagramDoubleClicked(Model? model, PointerEventArgs e)
    {
        try
        {
            if (model is not null) return;
            
            var point = _context.View.GetRelativeMousePoint(e.ClientX, e.ClientY);
            var position = new NodePosition { X = point.X, Y = point.Y };
            
            _logger.LogInformation("Double clicked diagram {NodePosition}", position);

            var nodeIdentifier = NodeIdentifier.NewIdentifier();
            var (typeTagGroupId, typeTagId, assignTypeTag) = CreateTagAndGroup(WellKnownTagGroup.Type, "Default");
            var (statusTagGroupId, statusTagId, assignStatusTag) = CreateTagAndGroup(WellKnownTagGroup.Status, "None");
            var (layerTagGroupId, layerTagId, assignLayerTag) = CreateTagAndGroup(WellKnownTagGroup.Layer, "Default");
            
            Command[] commands =
            [
                AddNodeCommandHandler.CreateCommand(_context, position, nodeIdentifier, "New element"),
                AddTagGroupCommandHandler.CreateCommand(_context, nodeIdentifier, typeTagGroupId, WellKnownTagGroup.Type, 0, TagGroupMode.Single),
                assignTypeTag 
                    ? AssignTagCommandHandler.CreateCommand(_context, typeTagGroupId, typeTagId)
                    : AddTagCommandHandler.CreateCommand(_context, typeTagGroupId, typeTagId, "Default"),
                
                AddTagGroupCommandHandler.CreateCommand(_context, nodeIdentifier, statusTagGroupId, WellKnownTagGroup.Status, 1, TagGroupMode.Single),
                assignStatusTag 
                    ? AssignTagCommandHandler.CreateCommand(_context, statusTagGroupId, statusTagId)
                    : AddTagCommandHandler.CreateCommand(_context, statusTagGroupId, statusTagId, "None"),
                
                AddTagGroupCommandHandler.CreateCommand(_context, nodeIdentifier, layerTagGroupId, WellKnownTagGroup.Layer, 2, TagGroupMode.Multiple),
                assignLayerTag 
                    ? AssignTagCommandHandler.CreateCommand(_context, layerTagGroupId, layerTagId)
                    : AddTagCommandHandler.CreateCommand(_context, layerTagGroupId, layerTagId, "Default"),
            ];
            _context.Commands.Handle(commands);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Unable to handle {MethodName}", nameof(OnDiagramDoubleClicked));
        }
    }
}