using Blazor.Diagrams.Core.Models.Base;

namespace EtAlii.Adp.Client;
using Cn = CommandName;

public class ZoomCommand : ICommandHandler
{
    public string CommandName => Cn.Zoom;
    
    private readonly Diagram _diagram;
    private readonly DiagramView _view;
    private readonly ILogger _logger;

    public ZoomCommand(
        Diagram diagram, 
        DiagramView view,
        ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<ZoomCommand>();

        _diagram = diagram;
        _view = view;
    }

    public Task<Change[]> Execute(SelectableModel[] selection)
    {
        _logger.LogInformation("Zooming diagram from {OldZoom} to {NewZoom}", _diagram.Zoom, _view.Zoom);

        var change = DiagramZoomChange.Apply(_diagram, _view.Zoom);
        
        return Task.FromResult(new[] { change });
    }
}