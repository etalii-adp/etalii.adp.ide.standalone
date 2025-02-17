using BlazorBootstrap;
using Microsoft.AspNetCore.Components;

namespace EtAlii.Adp.Client;

public partial class NodeWidget
{
    [Parameter] public NodeView Node { get; set; } = null!;
    
    private string _nodeName = string.Empty;
    private bool _isEditing;

    private TextInput _textInput = null!;

    protected override void OnParametersSet()
    {
        _nodeName = Node.Name;
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