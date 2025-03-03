using Microsoft.AspNetCore.Components;

namespace EtAlii.Adp.Client;

public partial class TagsInput : ComponentBase
{
    private readonly List<Tag> _selectedTags = new();
    private bool _canAdd;
    private ILogger<DiagramPage> _logger = null!;

    [Parameter]
    public bool Disabled { get; set; }
    
    [Parameter] public TagGroup Value { get; set; } = null!;

    [Parameter] public EventCallback<TagGroup> ValueChanged { get; set; }

    [CascadingParameter] public DiagramContext Context { get; set; } = null!;

    [Inject] private ILoggerFactory LoggerFactory { get; set; } = null!;

    protected override void OnParametersSet()
    {
        _logger = LoggerFactory.CreateLogger<DiagramPage>();
        
        _selectedTags.Clear();
        if (Value == null!) return;
        _selectedTags.AddRange(Value.Tags);
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
    
    private void AddTag(Tag tag)
    {
        _selectedTags.Add(tag);
        _selectedTags.Sort();

        var command = AddTagCommandHandler.CreateCommand(Context, Value.Id, tag.Id, tag.Name); 
        Context.Commands.Handle(command);
        UpdateCanAdd();
    }

    private void RemoveTag(Tag tagToRemove)
    {
        _selectedTags.Remove(tagToRemove);
        
        var command = RemoveTagCommandHandler.CreateCommand(Context, Value.Id, tagToRemove.Id, tagToRemove.Name); 
        Context.Commands.Handle(command);
        UpdateCanAdd();
    }
}