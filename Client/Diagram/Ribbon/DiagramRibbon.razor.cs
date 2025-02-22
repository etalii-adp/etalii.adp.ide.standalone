using Blazor.Diagrams.Core.Models.Base;
using BlazorBootstrap;
using Microsoft.AspNetCore.Components;

namespace EtAlii.Adp.Client;

public partial class DiagramRibbon : ComponentBase
{
    private bool _canGroup;
    private bool _canUngroup;
    private bool _canAlign;
    
    private DiagramContext _context = null!;

    public static void Initialize(DiagramRibbon ribbon, DiagramContext context)
    {
        ribbon._context = context;
        ribbon.StateHasChanged();

        context.View.SelectionChanged += ribbon.OnSelectionChanged;
        context.History.Changed += ribbon.OnHistoryChanged;
    }

    protected override void OnParametersSet()
    {
        OnHistoryChanged();
    }
    
    private void OnRibbonItemClick(RibbonItemEventArgs e)
    {
        var handler = _context.CommandHandlers
            .OfType<IRibbonCommandHandler>()
            .Single(ch => ch.CommandName == e.Name);
        var commands = handler.CreateCommands(_context);
        _context.Commands.Handle(commands);
    }

    private void OnHistoryChanged()
    {
        StateHasChanged();
    }

    private void OnSelectionChanged(SelectableModel obj)
    {
        _canGroup = _context.Selection.OfType<NodeView>().Count() > 1;
        _canUngroup = false;// selectedObjects.OfType<GroupView>().Count() > 1;
        _canAlign = _context.Selection.Length > 1 && _context.Selection.Length == _context.Selection.OfType<NodeView>().Count();
        
        StateHasChanged();
    }
}