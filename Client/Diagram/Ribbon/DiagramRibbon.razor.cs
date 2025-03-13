using Blazor.Diagrams.Core.Models.Base;
using BlazorBootstrap;
using Microsoft.AspNetCore.Components;

namespace EtAlii.Adp.Client;

public partial class DiagramRibbon : ComponentBase
{
    private DiagramContext _context = null!;
    private readonly ILogger _logger;

    private bool _isReadOnly;
    
    public DiagramRibbon(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<DiagramRibbon>();
    }
    
    public void Initialize(DiagramContext context)
    {
        _isReadOnly = context.Diagram.IsReadOnly;

        _context = context;
        StateHasChanged();

        context.SelectionChanged += StateHasChanged;
        context.History.Changed += StateHasChanged;
    }

    public void Deinitialize()
    {
        _context.SelectionChanged -= StateHasChanged;
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

        handler.RaiseClicked(commands);
        
        _context.Commands.Handle(commands);
    }
}