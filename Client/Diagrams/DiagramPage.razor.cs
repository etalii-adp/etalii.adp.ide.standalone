using System.Numerics;
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
            Zoom =
            {
                Enabled = false,
            },
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
        Diagram.SetPan(_diagram.Position.Coordinates.X, _diagram.Position.Coordinates.Y);
        Diagram.ZoomChanged += OnDiagramZoomed;
        Diagram.PanChanged += OnDiagramPanned;
        
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

    private async void OnDiagramZoomed()
    {
        try
        {
            var change = MapZoomChange.Apply(_diagram, (float)Diagram.Zoom);

            // Throttled save.
            await ChangePusher.Enqueue(change);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Unable to handle {MethodName}", nameof(OnDiagramZoomed));
        }
    }

    private async void OnDiagramPanned()
    {
        try
        {
            var newPosition = (DiagramPosition)new Vector2 { X = (float)Diagram.Pan.X, Y = (float)Diagram.Pan.Y };
            var change = MapPositionChange.Apply(_diagram, newPosition);

            // Throttled save.
            await ChangePusher.Enqueue(change);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Unable to handle {MethodName}", nameof(OnDiagramPanned));
        }
    }
}