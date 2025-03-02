using System.Net.Http.Json;
using Microsoft.AspNetCore.Components;

namespace EtAlii.Adp.Client;

public partial class DiagramPage : IDisposable
{
    [Parameter] public string DiagramTitle { get; set; } = null!;
    
    [Inject] private ILoggerFactory LoggerFactory { get; set; } = null!;

    [Inject] private HttpClient Client { get; set; } = null!;

    [Inject] private DiagramManager DiagramManager { get; set; } = null!;
    
    [Inject] private HistoryManager HistoryManager { get; set; } = null!;
    
    [Inject] private CommandManager CommandManager { get; set; } = null!;
    [Inject] private NodeManager NodeManager { get; set; } = null!;
    [Inject] private LinkManager LinkManager { get; set; } = null!;
    
    [Inject] private DiagramView DiagramView { get; set; } = null!;
    [Inject] private IEnumerable<ICommandHandler> CommandHandlers { get; set; } = null!;
    
    private ILogger _logger = null!;
    private DiagramRibbon _ribbon = null!;
    private DiagramContext _context = null!;
    private Diagram _currentDiagram = null!;

    protected override async Task OnParametersSetAsync()
    {
        _logger = LoggerFactory.CreateLogger<DiagramPage>();
        
        _logger.LogInformation("Diagram page parameters set");
        
        _currentDiagram = DiagramManager.CurrentDiagram!;
        var updatedDiagram = (await Client.GetFromJsonAsync<Diagram>(ApplicationApi.Diagram.Content.Request(_currentDiagram.Id)))!;
        _currentDiagram.Initialize(updatedDiagram);

        _context = new DiagramContext
        {
            Diagram = _currentDiagram,
            Ribbon = _ribbon,
            History = HistoryManager,
            Nodes = NodeManager,
            NodeFactory = new NodeFactory(), // Should become typed per diagram.
            Links = LinkManager,
            View = DiagramView,
            Commands = CommandManager,
            CommandHandlers = CommandHandlers.ToArray()
        };
        
        _logger.LogInformation("Initializing subsystems");

        _context.View.Initialize(_context);
        _context.Nodes.Initialize(_context);
        _context.Links.Initialize(_context);
        _context.Commands.Initialize(_context);
        _context.Ribbon.Initialize(_context);
    }

    public void Dispose()
    {
        _logger.LogInformation("Deinitializing subsystems");

        _context.View.DeInitialize();
        _context.Nodes.Deinitialize();
        _context.Links.Deinitialize();
        _context.Commands.Deinitialize();
        _context.Ribbon.Deinitialize();
    }
}