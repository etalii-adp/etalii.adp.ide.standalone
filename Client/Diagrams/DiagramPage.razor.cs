using System.Net.Http.Json;
using Microsoft.AspNetCore.Components;

namespace EtAlii.Adp.Client;

public partial class DiagramPage
{
    [Parameter] public string DiagramTitle { get; set; } = null!;
    
    private DiagramView _view = null!;

    [Inject] private ILoggerFactory LoggerFactory { get; set; } = null!;

    [Inject] private HttpClient Client { get; set; } = null!;

    [Inject] private DiagramManager DiagramManager { get; set; } = null!;
    
    [Inject] private ChangePusher ChangePusher { get; set; } = null!;
    
    private ILogger _logger = null!;
    
    private NodeManager _nodeManager = null!;
    private ViewManager _viewManager = null!;
    private LinkManager _linkManager = null!;

    protected override async Task OnParametersSetAsync()
    {
        _logger = LoggerFactory.CreateLogger<DiagramPage>();
        
        _logger.LogInformation("Diagram page parameters set");

        _view = new DiagramView
        {
            Diagram = DiagramManager.CurrentDiagram!
        };
        
        var updatedDiagram = (await Client.GetFromJsonAsync<Diagram>(ApplicationApi.Diagram.Content.Request(_view.Diagram.Id)))!;
        _view.Diagram.Update(updatedDiagram);
        
        _viewManager = new ViewManager(_view, _view.Diagram, ChangePusher, LoggerFactory);
        await _viewManager.Initialize();

        _nodeManager = new NodeManager(_view, _view.Diagram, ChangePusher, LoggerFactory);
        await _nodeManager.Initialize();
        
        _linkManager = new LinkManager(_view, _view.Diagram, ChangePusher, LoggerFactory);
        await _linkManager.Initialize();

        // var firstNode = Diagram.Nodes.Add(new NodeModel(position: new Point(50, 50)) { Title = "Node 1" });
        // var secondNode = Diagram.Nodes.Add(new NodeModel(position: new Point(200, 100)) { Title = "Node 2" });
        // var leftPort = secondNode.AddPort(PortAlignment.Left);
        // var rightPort = secondNode.AddPort(PortAlignment.Right);
        //
        // // The connection point will be the intersection of
        // // a line going from the target to the center of the source
        // var sourceAnchor = new ShapeIntersectionAnchor(firstNode);
        // // The connection point will be the port's position
        // var targetAnchor = new SinglePortAnchor(leftPort);
        // var link = Diagram.Links.Add(new LinkModel(sourceAnchor, targetAnchor));
    }

}