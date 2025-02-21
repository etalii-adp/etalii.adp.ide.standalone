using Blazor.Diagrams.Core.Models.Base;

namespace EtAlii.Adp.Client;
using Cn = CommandName;

public class PanCommand : ICommandHandler
{
    public string CommandName => Cn.Pan;
    
    private readonly Diagram _diagram;
    private readonly DiagramView _view;
    private readonly ILogger _logger;

    public PanCommand(
        Diagram diagram, 
        DiagramView view,
        ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<PanCommand>();

        _diagram = diagram;
        _view = view;
    }

    public Task<Change[]> Execute(SelectableModel[] selection)
    {
        var newPosition = new DiagramPosition { X = _view.Pan.X, Y = _view.Pan.Y };
            
        _logger.LogInformation("Panning diagram to {DiagramPosition}", newPosition);

        var change = DiagramPositionChange.Apply(_diagram, newPosition);
        
        return Task.FromResult(new[] { change });
    }
}