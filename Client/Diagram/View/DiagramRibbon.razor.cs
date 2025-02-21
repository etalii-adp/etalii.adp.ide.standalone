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
    
    private DiagramSelection _selection = DiagramSelection.Nothing;

    public event Action<string>? RibbonClicked;
    
    
    protected override void OnParametersSet()
    {
        UpdateBasedOnSelection([]);
    }

    public void UpdateBasedOnHistory(HistoryManager historyManager)
    {
        _canUndo = historyManager.History > 0;
        _canRedo = historyManager.Future > 0;
        StateHasChanged();
    }
    
    public void UpdateBasedOnSelection(SelectableModel[] selectedObjects)
    {
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

    private void OnRibbonItemClick(RibbonItemEventArgs e) => RibbonClicked?.Invoke(e.Name!);
}