using BlazorBootstrap;
using Microsoft.AspNetCore.Components;

namespace EtAlii.Adp.Client;

public partial class DiagramCard : ComponentBase
{
    [Parameter] public required Diagram Diagram { get; init; }
    [Parameter] public EventCallback<Diagram> Edited { get; set; }
    [Parameter] public EventCallback<Diagram> Deleted { get; set; }
    [Parameter] public EventCallback<Diagram> View { get; set; }

    private bool _isEditingName;
    private string _diagramName = null!;
    private TextInput? _nameInput;

    private bool _isEditingDescription;
    private string _diagramDescription = null!;
    private TextInput? _descriptionInput;

    protected override void OnParametersSet()
    {
        _diagramName = Diagram.Name;
        _diagramDescription = Diagram.Description;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_nameInput != null && _isEditingName)
        {
            await _nameInput.Element.FocusAsync();
        }
        if (_descriptionInput != null && _isEditingDescription)
        {
            await _descriptionInput.Element.FocusAsync();
        }
    }
    
    private void OnStartDescriptionEdit()
    {
        _isEditingDescription = true;
        StateHasChanged();
    }

    private void OnStartNameEdit()
    {
        _isEditingName = true;
        StateHasChanged();
    }

    private async Task OnEndNameEdit()
    {
        _isEditingName = false;
        Diagram.Name = _diagramName;
        StateHasChanged();

        await Edited.InvokeAsync(Diagram);
    }

    
    private async Task OnEndDescriptionEdit()
    {
        _isEditingDescription = false;
        Diagram.Description = _diagramDescription;
        StateHasChanged();

        await Edited.InvokeAsync(Diagram);
    }

    private async Task EditDiagram(Diagram diagram)
    {
        await Edited.InvokeAsync(diagram);
    }

    private async Task DeleteDiagram(Diagram diagram)
    {
        await Deleted.InvokeAsync(diagram);
    }

    private async Task ViewDiagram(Diagram diagram)
    {
        await View.InvokeAsync(diagram);
    }
}