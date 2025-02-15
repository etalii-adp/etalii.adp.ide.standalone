using Blazor.Diagrams;
using Blazor.Diagrams.Core.Anchors;
using Blazor.Diagrams.Core.Geometry;
using Blazor.Diagrams.Core.Models;
using Blazor.Diagrams.Core.PathGenerators;
using Blazor.Diagrams.Core.Routers;
using Blazor.Diagrams.Options;
using BlazorBootstrap;
using Microsoft.AspNetCore.Components;

namespace EtAlii.Adp.Client;

public partial class DiagramPage
{
    [Parameter] public string DiagramTitle { get; set; } = null!;
    
    protected BlazorDiagram Diagram { get; set; } = null!;

    // private string? _selectedRibbonItem;
    [Inject] private ILoggerFactory LoggerFactory { get; set; } = null!;

    [Inject] private DiagramManager DiagramManager { get; set; } = null!;
    
    [Inject] private ChangePusher ChangePusher { get; set; } = null!;
    
    private Diagram _diagram = null!;
    
    private ILogger _logger = null!;
    
    private void OnRibbonItemClick(RibbonItemEventArgs args)
    {
        // _selectedRibbonItem = args.Name;
    }

    protected override void OnInitialized()
    {
        _logger = LoggerFactory.CreateLogger<DiagramPage>();
        
        var options = new BlazorDiagramOptions
        {
            AllowMultiSelection = true,
            Zoom = { Enabled = true },
            Links =
            {
                DefaultRouter = new NormalRouter(),
                DefaultPathGenerator = new SmoothPathGenerator()
            },
        };
        Diagram = new BlazorDiagram(options);
    }

    protected override void OnParametersSet()
    {
        _diagram = DiagramManager.CurrentDiagram!;
        Diagram.SetZoom(_diagram.Zoom <= 0f ? 1f : _diagram.Zoom);
        Diagram.SetPan(_diagram.Position.X, _diagram.Position.Y);
        Diagram.ZoomChanged += OnDiagramZoomed;
        Diagram.PanChanged += OnDiagramPanned;
        Diagram.PointerDoubleClick += OnDiagramDoubleClicked;
        
        var firstNode = Diagram.Nodes.Add(new NodeModel(position: new Point(50, 50)) { Title = "Node 1" });
        var secondNode = Diagram.Nodes.Add(new NodeModel(position: new Point(200, 100)) { Title = "Node 2" });
        var leftPort = secondNode.AddPort(PortAlignment.Left);
        var rightPort = secondNode.AddPort(PortAlignment.Right);
        
        // The connection point will be the intersection of
        // a line going from the target to the center of the source
        var sourceAnchor = new ShapeIntersectionAnchor(firstNode);
        // The connection point will be the port's position
        var targetAnchor = new SinglePortAnchor(leftPort);
        var link = Diagram.Links.Add(new LinkModel(sourceAnchor, targetAnchor));
    }

}