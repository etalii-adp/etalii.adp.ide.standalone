using Microsoft.AspNetCore.Components;

namespace EtAlii.Adp.Client;

public partial class TagsInput : ComponentBase
{
    private readonly List<string> _selectedTags = new();
    private bool _canAdd;

    [Parameter]
    public bool Disabled { get; set; }
    
    [Parameter] public TagGroup Value { get; set; } = null!;

    [Parameter] public EventCallback<TagGroup> ValueChanged { get; set; }

    [CascadingParameter] public DiagramContext Context { get; set; } = null!;

    protected override void OnParametersSet()
    {
        _selectedTags.Clear();
        if (Value == null!) return;
        _selectedTags.AddRange(Value.Tags.Select(g => g.Name));
        _selectedTags.Sort();
        UpdateCanAdd();
        
        _allAvailableTags.Clear();
        _allAvailableTags.AddRange(
        [
            new Tag { Id = TagIdentifier.NewIdentifier(), Name = "System",},
            new Tag { Id = TagIdentifier.NewIdentifier(), Name = "Test",},
            new Tag { Id = TagIdentifier.NewIdentifier(), Name = "Default",}
        ]);
    }

    private void UpdateCanAdd()
    {
        _canAdd = Value.Mode == TagGroupMode.Multiple || _selectedTags.Count == 0;
        StateHasChanged();
    }
    
    private void AddTag(string tagName)
    {
        var tagToAdd = tagName; 
        _selectedTags.Add(tagToAdd);
        _selectedTags.Sort();
        _newTagName = "";

        var tagId = TagIdentifier.NewIdentifier();
        var command = AddTagCommandHandler.CreateCommand(Context, Value.Id, tagId, tagToAdd); 
        Context.Commands.Handle(command);
        UpdateCanAdd();
    }

    private void RemoveTag(string tagNameToRemove)
    {
        _selectedTags.Remove(tagNameToRemove);
        
        var tag = Value.Tags.Single(t => t.Name == tagNameToRemove);
        var command = RemoveTagCommandHandler.CreateCommand(Context, Value.Id, tag.Id, tagNameToRemove); 
        Context.Commands.Handle(command);
        UpdateCanAdd();
    }
}