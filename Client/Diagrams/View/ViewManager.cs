using Blazor.Diagrams.Core.Models.Base;

namespace EtAlii.Adp.Client;

public partial class ViewManager
{
    private readonly DiagramView _view;
    private readonly Diagram _diagram;
    private readonly DiagramRibbon _ribbon;
    private readonly ChangePusher _changePusher;
    private readonly ILogger _logger;

    private readonly ICommandHandler[] _commandHandlers;
    public ViewManager(
        DiagramView view, 
        Diagram diagram, 
        DiagramRibbon ribbon, 
        ChangePusher changePusher, 
        ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<ViewManager>();
        _view = view;
        _view.SelectionChanged += OnSelectionChanged;
        _diagram = diagram;
        _ribbon = ribbon;
        _ribbon.RibbonClicked += HandleCommand;
        _changePusher = changePusher;

        _commandHandlers =
        [
            new DeleteCommandHandler(_view, _diagram, _changePusher)
        ];
    }

    private void OnSelectionChanged(SelectableModel obj)
    {
        _ribbon.UpdateBasedOnSelection(_view.GetSelectedModels().ToArray());
    }

    public async Task Initialize()
    {
        _logger.LogInformation("Initializing view management");
        InitializePan();
        InitializeZoom();
        
        await Task.CompletedTask;
    }

    private void HandleCommand(string commandName)
    {
        var handler = _commandHandlers.Single(ch => ch.CommandName == commandName);
        handler.Execute();
    }
}
    
