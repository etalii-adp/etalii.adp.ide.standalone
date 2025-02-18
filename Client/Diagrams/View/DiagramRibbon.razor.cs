using BlazorBootstrap;
using Microsoft.AspNetCore.Components;

namespace EtAlii.Adp.Client;

public partial class DiagramRibbon : ComponentBase
{
    // private string? _selectedRibbonItem;

    [Parameter] public object[] SelectedObjects { get; set; } = null!;

    private RibbonItem? _deleteButton;
    private bool _canDelete;
    private void OnRibbonItemClick(RibbonItemEventArgs args)
    {
    }

    protected override void OnAfterRender(bool firstRender)
    {
        base.OnAfterRender(firstRender);
    }

    protected override void OnParametersSet()
    {
        _canDelete = false;
        StateHasChanged();
        //_deleteButton;
    }
}