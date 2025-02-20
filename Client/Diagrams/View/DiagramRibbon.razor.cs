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

    public event Action<string>? RibbonClicked;
    
    protected override void OnParametersSet()
    {
        UpdateBasedOnSelection([]);
    }

    public void UpdateBasedOnSelection(SelectableModel[] selectedObjects)
    {
        _canDelete = selectedObjects.Any();
        _canGroup = selectedObjects.OfType<NodeView>().Count() > 1;
        _canUngroup = false;// selectedObjects.OfType<GroupView>().Count() > 1;
        _canAlign = selectedObjects.Length != 0 && selectedObjects.Length == selectedObjects.OfType<NodeView>().Count();
        
        StateHasChanged();
    }

    private void OnRibbonItemClick(RibbonItemEventArgs e) => RibbonClicked?.Invoke(e.Name!);
}