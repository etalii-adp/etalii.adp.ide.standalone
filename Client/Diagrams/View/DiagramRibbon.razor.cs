using Blazor.Diagrams.Core.Models.Base;
using BlazorBootstrap;
using Microsoft.AspNetCore.Components;

namespace EtAlii.Adp.Client;

public partial class DiagramRibbon : ComponentBase
{
    private bool _canDelete;

    public event Action<string>? RibbonClicked;
    
    protected override void OnParametersSet()
    {
        UpdateBasedOnSelection([]);
    }

    public void UpdateBasedOnSelection(SelectableModel[] selectedObjects)
    {
        _canDelete = selectedObjects.Any();
        StateHasChanged();
        //_deleteButton;
    }

    private void OnRibbonItemClick(RibbonItemEventArgs e) => RibbonClicked?.Invoke(e.Name!);
}