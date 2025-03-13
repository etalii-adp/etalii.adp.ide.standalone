using System.Net.Http.Json;
using BlazorBootstrap;
using Microsoft.AspNetCore.Components;

namespace EtAlii.Adp.Client;

public class DiagramPageBase : ComponentBase, IDisposable
{
    [Parameter] public string DiagramTitle { get; set; } = null!;

    [Parameter] public Guid DiagramId { get; set; }
    [Inject] private ILoggerFactory LoggerFactory { get; set; } = null!;

    [Inject] private HttpClient Client { get; set; } = null!;

    [Inject] private DiagramManager DiagramManager { get; set; } = null!;
    
    [Inject] private HistoryManager HistoryManager { get; set; } = null!;
    
    [Inject] private CommandManager CommandManager { get; set; } = null!;
    [Inject] private NodeManager NodeManager { get; set; } = null!;
    [Inject] private LinkManager LinkManager { get; set; } = null!;
    
    [Inject] protected DiagramView DiagramView { get; set; } = null!;
    [Inject] private IEnumerable<ICommandHandler> CommandHandlers { get; set; } = null!;
    
    [Inject] private ToastService ToastService { get; set; } = null!;

    private ILogger _logger = null!;
    protected DiagramRibbon Ribbon = null!;
    protected DiagramContext Context = null!;
    protected Diagram? CurrentDiagram;
    protected bool IsLoaded;

    protected override async Task OnParametersSetAsync()
    {
        var diagramId = new DiagramIdentifier { Identifier = DiagramId };
        CurrentDiagram = (await Client.GetFromJsonAsync<Diagram>(ApplicationApi.Diagram.Content.Request(diagramId)))!;
        
        Context = new DiagramContext
        {
            Diagram = CurrentDiagram,
            Ribbon = Ribbon,
            History = HistoryManager,
            Nodes = NodeManager,
            NodeFactory = new NodeFactory(), // Should become typed per diagram.
            Links = LinkManager,
            View = DiagramView,
            ToastService = ToastService,
            Commands = CommandManager,
            CommandHandlers = CommandHandlers.ToArray(),
        };
        
        _logger.LogInformation("Initializing subsystems");

        Context.View.Initialize(Context);
        Context.Nodes.Initialize(Context);
        Context.Links.Initialize(Context);
        Context.Commands.Initialize(Context);
        if (Context.Ribbon != null!)
        {
            Context.Ribbon.Initialize(Context);
        }

        IsLoaded = true;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
        }
        
        await base.OnAfterRenderAsync(firstRender);
    }

    protected override void OnParametersSet()
    {
        _logger = LoggerFactory.CreateLogger<DiagramPage>();
        _logger.LogInformation("Diagram page parameters set");
    }

    public void Dispose()
    {
        _logger.LogInformation("Deinitializing subsystems");

        Context.View.DeInitialize();
        Context.Nodes.Deinitialize();
        Context.Links.Deinitialize();
        Context.Commands.Deinitialize();
        if (Context.Ribbon != null!)
        {
            Context.Ribbon.Deinitialize();
        }
    }
}