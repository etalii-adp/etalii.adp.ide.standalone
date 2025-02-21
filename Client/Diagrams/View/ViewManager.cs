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
            new RenameCommandHandler(),
            new UndoCommandHandler(_history),
            new RedoCommandHandler(_history)
        ];
    }

    public async Task Initialize()
    {
        _logger.LogInformation("Initializing view management");
        InitializePan();
        InitializeZoom();
        
        await Task.CompletedTask;
    }
}
    
