namespace EtAlii.Adp.Client;

public partial class ViewManager
{
    private readonly DiagramView _view;
    private readonly Diagram _diagram;
    private readonly ChangePusher _changePusher;
    private readonly ILogger _logger;

    public ViewManager(
        DiagramView view, Diagram diagram, 
        ChangePusher changePusher, ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<ViewManager>();
        _view = view;
        _diagram = diagram;
        _changePusher = changePusher;
    }

    public async Task Initialize()
    {
        _logger.LogInformation("Initializing view management");
        InitializePan();
        InitializeZoom();
        
        await Task.CompletedTask;
    }
}
    
