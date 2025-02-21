namespace EtAlii.Adp.Client;

public partial class ViewManager
{
    private readonly DiagramView _view;
    private readonly Diagram _diagram;
    private readonly DiagramRibbon _ribbon;
    private readonly HistoryManager _history;
    private readonly ILogger _logger;

    private readonly ICommandHandler[] _commandHandlers;
    
    public ViewManager(
        DiagramView view, 
        Diagram diagram, 
        DiagramRibbon ribbon, 
        HistoryManager history,
        ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<ViewManager>();
        _view = view;
        _view.SelectionChanged += OnSelectionChanged;
        _diagram = diagram;
        _ribbon = ribbon;
        _ribbon.RibbonClicked += HandleCommand;
        _history = history;
        _history.Changed += OnHistoryChanged;

        _commandHandlers =
        [
            new DeleteCommandHandler(_view, _diagram),
            new StartNodeRenameCommandHandler(),
            new UndoCommandHandler(_history),
            new RedoCommandHandler(_history),
            new PanCommand(_diagram, _view, loggerFactory),
            new ZoomCommand(_diagram, _view, loggerFactory)
        ];
    }

    public async Task Initialize()
    {
        _logger.LogInformation("Initializing view management");
        InitializePan();
        InitializeZoom();
        
        await Task.CompletedTask;
    }
    
    private async void HandleCommand(string commandName)
    {
        try
        {
            _logger.LogInformation("Handling {CommandName} command", commandName);

            var selection = _view
                .GetSelectedModels()
                .ToArray();
            var handler = _commandHandlers.Single(ch => ch.CommandName == commandName);
            var changes = await handler.Execute(selection);
            await _history.Push(changes);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Unable to handle {CommandName} command", commandName);
        }
    }
}
    
