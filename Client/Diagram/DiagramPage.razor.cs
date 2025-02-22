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
    
    [Inject] private HistoryManager HistoryManager { get; set; } = null!;
    
    private ILogger _logger = null!;
    
    private NodeManager _nodeManager = null!;
    private ViewManager _viewManager = null!;
    private LinkManager _linkManager = null!;
    private DiagramRibbon _ribbon = null!;

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

        var panCommandHandler = new PanCommandHandler(LoggerFactory);
        var zoomCommandHandler = new ZoomCommandHandler(LoggerFactory);
        var addNodeCommandHandler = new AddNodeCommandHandler(LoggerFactory);
        var removeNodeCommandHandler = new RemoveNodeCommandHandler(addNodeCommandHandler, LoggerFactory);
        var commandHandlers = new ICommandHandler[]
        {
            new DeleteCommandHandler(),
            new StartNodeRenameCommandHandler(),
            new UndoCommandHandler(HistoryManager),
            new RedoCommandHandler(HistoryManager),
            panCommandHandler,
            zoomCommandHandler,
            
            addNodeCommandHandler,
            removeNodeCommandHandler,
            new RenameNodeCommandHandler(LoggerFactory),
            new MoveNodeCommandHandler(LoggerFactory),
            
            new AddLinkCommandHandler(LoggerFactory),
            new RemoveLinkCommandHandler(LoggerFactory),
        };

        var commandManager = new CommandManager(HistoryManager, LoggerFactory);
        
        var context = new DiagramContext
        {
            Diagram = _view.Diagram,
            Ribbon = _ribbon,
            History = HistoryManager,
            View = _view,
            Commands = commandManager,
            CommandHandlers = commandHandlers,
        };
        
        _viewManager = new ViewManager(context, LoggerFactory);
        await _viewManager.Initialize();

        _nodeManager = new NodeManager(context, LoggerFactory);
        await _nodeManager.Initialize();
        
        _linkManager = new LinkManager(context, LoggerFactory);
        await _linkManager.Initialize();

        CommandManager.Initialize(commandManager, context);
        DiagramRibbon.Initialize(_ribbon, context);
        PanCommandHandler.Initialize(panCommandHandler, context);
        ZoomCommandHandler.Initialize(zoomCommandHandler, context);
    }
}