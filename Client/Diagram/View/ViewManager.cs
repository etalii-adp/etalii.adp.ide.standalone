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

        _context.View.SelectionChanged += _ => UpdateContext();
        
        await Task.CompletedTask;
    }

    private void UpdateContext()
    {
        var selectedObjects = _context.View.GetSelectedModels().ToArray();

        DiagramSelection selectionType;
        
        if (selectedObjects.Length == 1 && selectedObjects.Length == selectedObjects.OfType<NodeView>().Count())
        {
            selectionType = DiagramSelection.SingleNode;            
        }
        else if (selectedObjects.Length > 1 && selectedObjects.Length == selectedObjects.OfType<NodeView>().Count())
        {
            selectionType = DiagramSelection.MultipleNodes;            
        }
        else if (selectedObjects.Length == 1 && selectedObjects.Length == selectedObjects.OfType<LinkView>().Count())
        {
            selectionType = DiagramSelection.SingleLink;            
        }
        else if (selectedObjects.Length > 1 && selectedObjects.Length == selectedObjects.OfType<LinkView>().Count())
        {
            selectionType = DiagramSelection.MultipleLinks;            
        }
        else if (selectedObjects.OfType<LinkView>().Any() && selectedObjects.OfType<NodeView>().Any())
        {
            selectionType = DiagramSelection.NodesAndLinks;            
        }
        else
        {
            selectionType = DiagramSelection.Nothing;
        }

        _context.Selection = selectedObjects;
        _context.SelectionType = selectionType;
        
        _context.CanGroup = _context.Selection.OfType<NodeView>().Count() > 1;
        _context.CanUngroup = false;// selectedObjects.OfType<GroupView>().Count() > 1;

    }
}
    
