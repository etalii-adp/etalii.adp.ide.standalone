using Microsoft.AspNetCore.Components;

namespace EtAlii.Adp.Client;

public partial class NodeWidget
{
    [Parameter] public NodeView Node { get; set; } = null!;

    // [Inject] private ChangePusher ChangePusher { get; set; } = null!;
    
    private string _nodeName = string.Empty;
    private bool _isEditing;

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
        Node.Name = _nodeName;
        
        StateHasChanged();
        
        // NodeRenameChange.Apply()
        // var change = NodeAddChange.Apply(_diagram, nodeView.Position, nodeView.Id);
        // await ChangePusher.Enqueue(change);

        await Task.CompletedTask;
    }
}