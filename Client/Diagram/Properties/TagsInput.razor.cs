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

    [CascadingParameter] public DiagramContext Context { get; set; } = null!;

    protected override void OnInitialized()
    {
        if (Value != null!)
        {
            _tags.AddRange(Value.Tags.Select(g => g.Name));
            _tags.Sort();
        }
    }

    private void AddTag()
    {
        if (string.IsNullOrWhiteSpace(_newTag) || _tags.Contains(_newTag)) return;

        var tagToAdd = _newTag.Trim(); 
        _tags.Add(tagToAdd);
        _tags.Sort();
        _newTag = "";

        var tagId = TagIdentifier.NewIdentifier();
        var command = AddTagCommandHandler.CreateCommand(Context, Value.Id, tagId, tagToAdd); 
        Context.Commands.Handle(command);
    }

    private void RemoveTag(string tagNameToRemove)
    {
        _tags.Remove(tagNameToRemove);
        
        var tag = Value.Tags.Single(t => t.Name == tagNameToRemove);
        var command = RemoveTagCommandHandler.CreateCommand(Context, Value.Id, tag.Id, tagNameToRemove); 
        Context.Commands.Handle(command);

    }

    private void HandleKeyPress(KeyboardEventArgs e)
    {
        if (e.Key == "Enter")
        {
            AddTag();
        }
    }
}