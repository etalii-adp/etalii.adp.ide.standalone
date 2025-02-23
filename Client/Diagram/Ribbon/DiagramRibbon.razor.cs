using Blazor.Diagrams.Core.Models.Base;
using BlazorBootstrap;
using Microsoft.AspNetCore.Components;

namespace EtAlii.Adp.Client;

public partial class DiagramRibbon : ComponentBase
{
    private DiagramContext _context = null!;
    private readonly ILogger _logger;

    public DiagramRibbon(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<DiagramRibbon>();
    }
    
    public void Initialize(DiagramContext context)
    {
        _context = context;
        StateHasChanged();

        context.View.SelectionChanged += StateHasChanged;
        context.History.Changed += StateHasChanged;
    }

    public void Deinitialize()
    {
        _context.View.SelectionChanged -= StateHasChanged;
        _context.History.Changed -= StateHasChanged;
    }

    private void StateHasChanged(SelectableModel? _) => StateHasChanged();
    protected override void OnParametersSet() => StateHasChanged();
    
    private void OnRibbonItemClick(RibbonItemEventArgs e)
    {
        var handler = _context.CommandHandlers
            .OfType<IRibbonCommandHandler>()
            .SingleOrDefault(ch => ch.CommandName == e.Name);
        if (handler == null)
        {
            _logger.LogError("No handler found for {CommandName}", e.Name);
            return;
        }
        
        var commands = handler.CreateCommands(_context);
        _context.Commands.Handle(commands);
    }
}