namespace EtAlii.Adp.Client;

public class ViewManager
{
    private readonly ILogger _logger;
    
    private readonly DiagramContext _context;
    
    public ViewManager(DiagramContext context, ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<ViewManager>();
        _context = context;
    }

    public async Task Initialize()
    {
        _logger.LogInformation("Initializing view management");

        _context.View.SelectionChanged += _ =>
        {
            _context.Selection = _context.View.GetSelectedModels().ToArray();
        };
        
        await Task.CompletedTask;
    }
}
    
