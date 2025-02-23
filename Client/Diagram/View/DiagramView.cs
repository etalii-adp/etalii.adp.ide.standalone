using Blazor.Diagrams;
using Blazor.Diagrams.Components;
using Blazor.Diagrams.Core.Anchors;
using Blazor.Diagrams.Core.Models.Base;
using Blazor.Diagrams.Core.PathGenerators;
using Blazor.Diagrams.Core.Routers;
using Blazor.Diagrams.Options;

namespace EtAlii.Adp.Client;

public class DiagramView : BlazorDiagram
{
    private readonly ILogger _logger;
    
    private DiagramContext _context = null!;

    public bool ShowProperties { get; set; }
    
    public DiagramView(ILoggerFactory loggerFactory)
        : base(Options)
    {
        _logger = loggerFactory.CreateLogger<DiagramView>();
        
        RegisterComponent<NodeView, NodeWidget>();
        RegisterComponent<LinkView, LinkWidget>();
    }
    
    public void Initialize(DiagramContext context)
    {
        _context = context;
        _logger.LogInformation("Initializing view management");
        SelectionChanged += _ => UpdateContext();
        
        context.CommandHandlers
            .OfType<PanCommandHandler>()
            .Single()
            .Initialize(_context);

        context.CommandHandlers
            .OfType<ZoomCommandHandler>()
            .Single()
            .Initialize(_context);
    }

    private void UpdateContext()
    {
        _logger.LogInformation("Updating context");

        var selectedObjects = _context.View.GetSelectedModels().ToArray();

        DiagramSelection selectionType;
        
        if (selectedObjects.Length == 1 && selectedObjects.Length == selectedObjects.OfType<NodeView>().Count())
        {
            selectionType = DiagramSelection.SingleNode;            
        }
        else if (selectedObjects.Length > 1 && selectedObjects.Length == selectedObjects.OfType<NodeView>().Count())
        {
            selectionType = DiagramSelection.MultipleNodes;            
        }
        else if (selectedObjects.Length == 1 && selectedObjects.Length == selectedObjects.OfType<LinkView>().Count())
        {
            selectionType = DiagramSelection.SingleLink;            
        }
        else if (selectedObjects.Length > 1 && selectedObjects.Length == selectedObjects.OfType<LinkView>().Count())
        {
            selectionType = DiagramSelection.MultipleLinks;            
        }
        else if (selectedObjects.OfType<LinkView>().Any() && selectedObjects.OfType<NodeView>().Any())
        {
            selectionType = DiagramSelection.NodesAndLinks;            
        }
        else
        {
            selectionType = DiagramSelection.Nothing;
        }

        _context.Selection = selectedObjects;
        _context.SelectionType = selectionType;
        
        _context.CanGroup = _context.Selection.OfType<NodeView>().Count() > 1;
        _context.CanUngroup = false;// selectedObjects.OfType<GroupView>().Count() > 1;

    }
    
    private new static readonly BlazorDiagramOptions Options = new()
    {
        AllowMultiSelection = true,
        Zoom = { Enabled = true },
        Links =
        {
            Factory = CreateLink,
            EnableSnapping = true,
            DefaultRouter = new NormalRouter(),
            DefaultPathGenerator = new SmoothPathGenerator()
        },
    };

    private static BaseLinkModel CreateLink(Blazor.Diagrams.Core.Diagram diagram, ILinkable linkable, Anchor targetAnchor)
    {
        var source = new SinglePortAnchor((PortView)linkable);
        return new LinkView(source, targetAnchor);
    }
}