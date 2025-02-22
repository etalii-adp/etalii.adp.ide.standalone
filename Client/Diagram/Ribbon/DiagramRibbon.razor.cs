using Blazor.Diagrams.Core.Models.Base;
using BlazorBootstrap;
using Microsoft.AspNetCore.Components;

namespace EtAlii.Adp.Client;

public partial class DiagramRibbon : ComponentBase
{
    private bool _canDelete;
    private bool _canGroup;
    private bool _canUngroup;
    private bool _canAlign;
    private bool _canRename;

    private bool _canUndo;
    private bool _canRedo;

    private DiagramContext _context = null!;
    
    private DiagramSelection _selection = DiagramSelection.Nothing;
    
    protected override void OnParametersSet()
    {
        OnHistoryChanged();
    }
    
    private void OnRibbonItemClick(RibbonItemEventArgs e)
    {
        var commands = e.Name switch
        {
            CommandName.Delete => DeleteCommandHandler.CreateCommands(_context),
            CommandName.Rename => [StartNodeRenameCommandHandler.CreateCommand(_context)],
            CommandName.Undo => [UndoCommandHandler.CreateCommand(_context)],
            CommandName.Redo => [RedoCommandHandler.CreateCommand(_context)],
            _ => throw new ArgumentOutOfRangeException()
        };
        _context.Commands.Handle(commands);
    }

    public static void Initialize(DiagramRibbon ribbon, DiagramContext context)
    {
        ribbon._context = context;

        context.View.SelectionChanged += ribbon.OnSelectionChanged;
        context.History.Changed += ribbon.OnHistoryChanged;
    }

    private void OnHistoryChanged()
    {
        // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
        if (_context is not null)
        {
            _canUndo = _context.History.History > 0;
            _canRedo = _context.History.Future > 0;
        }
        StateHasChanged();
    }

    private void OnSelectionChanged(SelectableModel obj)
    {
        var selectedObjects = _context.Selection;
        
        if (selectedObjects.Length == 1 && selectedObjects.Length == selectedObjects.OfType<NodeView>().Count())
        {
            _selection = DiagramSelection.SingleNode;            
        }
        else if (selectedObjects.Length > 1 && selectedObjects.Length == selectedObjects.OfType<NodeView>().Count())
        {
            _selection = DiagramSelection.MultipleNodes;            
        }
        else if (selectedObjects.Length == 1 && selectedObjects.Length == selectedObjects.OfType<LinkView>().Count())
        {
            _selection = DiagramSelection.SingleLink;            
        }
        else if (selectedObjects.Length > 1 && selectedObjects.Length == selectedObjects.OfType<LinkView>().Count())
        {
            _selection = DiagramSelection.MultipleLinks;            
        }
        else if (selectedObjects.OfType<LinkView>().Any() && selectedObjects.OfType<NodeView>().Any())
        {
            _selection = DiagramSelection.NodesAndLinks;            
        }
        else
        {
            _selection = DiagramSelection.Nothing;
        }

        _canDelete = selectedObjects.Any();
        _canGroup = selectedObjects.OfType<NodeView>().Count() > 1;
        _canUngroup = false;// selectedObjects.OfType<GroupView>().Count() > 1;
        _canAlign = selectedObjects.Length > 1 && selectedObjects.Length == selectedObjects.OfType<NodeView>().Count();
        _canRename = selectedObjects.Length == 1 && selectedObjects.OfType<NodeView>().Any();
        
        StateHasChanged();
    }
}