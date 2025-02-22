using BlazorBootstrap;
using Microsoft.AspNetCore.Components;

namespace EtAlii.Adp.Client;

public partial class DiagramRibbon : ComponentBase
{
    private DiagramContext _context = null!;

    public static void Initialize(DiagramRibbon ribbon, DiagramContext context)
    {
        ribbon._context = context;
        ribbon.StateHasChanged();

        context.View.SelectionChanged += _ => ribbon.StateHasChanged();
        context.History.Changed += ribbon.StateHasChanged;
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