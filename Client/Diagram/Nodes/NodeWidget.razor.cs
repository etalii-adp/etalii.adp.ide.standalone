using BlazorBootstrap;
using Microsoft.AspNetCore.Components;

namespace EtAlii.Adp.Client;

public partial class NodeWidget : IDisposable
{
    [Parameter] public NodeView Node { get; set; } = null!;
    
    [CascadingParameter] public DiagramContext Context { get; set; } = null!;

    private string _nodeName = string.Empty;
    private bool _isEditing;
    private TextInput? _textInput;

    protected override void OnParametersSet()
    {
        _nodeName = Node.Name;
        if (!Node.Context.Diagram.IsReadOnly)
        {
            Node.EditRequested += OnStartNameEdit;
        }
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
        if (Node.Context.Diagram.IsReadOnly) return;

        _isEditing = true;
        StateHasChanged();
    }

    private void OnEndNameEdit()
    {
        _isEditing = false;
        var oldName = Node.Name;
        Node.Name = _nodeName;
        StateHasChanged();

        var command = RenameNodeCommandHandler.CreateCommand(Node.Id, oldName, _nodeName);
        Node.Context.Commands.Handle(command);
    }

    public void Dispose()
    {
        if (!Node.Context.Diagram.IsReadOnly)
        {
            Node.EditRequested -= OnStartNameEdit;
        }
    }
}