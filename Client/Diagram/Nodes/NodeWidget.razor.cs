using BlazorBootstrap;
using Microsoft.AspNetCore.Components;

namespace EtAlii.Adp.Client;

public partial class NodeWidget
{
    [Parameter] public NodeView Node { get; set; } = null!;
    
    private string _nodeName = string.Empty;
    private bool _isEditing;
    private TextInput? _textInput;

    protected override void OnParametersSet()
    {
        _nodeName = Node.Name;
        Node.EditRequested += OnStartNameEdit;
    }
    
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_textInput != null && _isEditing)
        {
            await _textInput.Element.FocusAsync();
        }
    }
    private void OnStartNameEdit()
    {
        _isEditing = true;
        StateHasChanged();
    }

    private async Task OnEndNameEdit()
    {
        _isEditing = false;
        var oldName = Node.Name;
        Node.Name = _nodeName;
        StateHasChanged();

        await Node.NodeManager.RenameNode(Node, oldName, _nodeName);
    }
}