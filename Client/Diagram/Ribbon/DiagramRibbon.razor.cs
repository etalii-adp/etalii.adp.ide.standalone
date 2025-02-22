using BlazorBootstrap;
using Microsoft.AspNetCore.Components;

namespace EtAlii.Adp.Client;

public partial class DiagramRibbon : ComponentBase
{
    private DiagramContext _context = null!;

    public void Initialize(DiagramContext context)
    {
        _context = context;
        StateHasChanged();

        context.View.SelectionChanged += _ => StateHasChanged();
        context.History.Changed += StateHasChanged;
    }

    protected override void OnParametersSet() => StateHasChanged();
    
    private void OnRibbonItemClick(RibbonItemEventArgs e)
    {
        var handler = _context.CommandHandlers
            .OfType<IRibbonCommandHandler>()
            .Single(ch => ch.CommandName == e.Name);
        var commands = handler.CreateCommands(_context);
        _context.Commands.Handle(commands);
    }
}