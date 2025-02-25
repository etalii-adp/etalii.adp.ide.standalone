using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace EtAlii.Adp.Client;

public partial class TagsInput : ComponentBase
{
    private readonly List<string> _tags = new();
    private string _newTag = "";

    [Parameter]
    public bool Disabled { get; set; }
    
    [Parameter] public TagGroup Value { get; set; } = null!;

    [Parameter] public EventCallback<TagGroup> ValueChanged { get; set; }
    
    
    private void AddTag()
    {
        if (string.IsNullOrWhiteSpace(_newTag) || _tags.Contains(_newTag)) return;
        
        _tags.Add(_newTag.Trim());
        _newTag = "";
    }

    private void RemoveTag(string tag)
    {
        _tags.Remove(tag);
    }

    private void HandleKeyPress(KeyboardEventArgs e)
    {
        if (e.Key == "Enter")
        {
            AddTag();
        }
    }
}