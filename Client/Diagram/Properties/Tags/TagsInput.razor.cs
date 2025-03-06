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
        
        var tagGroupName = Value.Name;

        var tagGroupsWithTags = Context.Diagram.Nodes
            .SelectMany(n => n.TagGroups)
            .Where(g => g.Name == tagGroupName && g.Tags.Count != 0)
            .Distinct()
            .ToArray();
            
        var tags = tagGroupsWithTags
            .SelectMany(g => g.Tags)
            .DistinctBy(t => t.Name)
            .OrderBy(t => t.Name)
            .ToArray();

        _allAvailableTags.AddRange(tags);
    }

    private void UpdateCanAdd()
    {
        _canAdd = Value.Mode == TagGroupMode.Multiple || _selectedTags.Count == 0;
        StateHasChanged();
    }
    
    private void AddNewTag(Tag tag)
    {
        _selectedTags.Add(tag);
        _selectedTags.Sort();

        var command = AddTagCommandHandler.CreateCommand(Context, Value.Id, tag.Id, tag.Name); 
        Context.Commands.Handle(command);
        UpdateCanAdd();
    }

    private async Task AssignExistingTag(Tag tag)
    {
        _selectedTags.Add(tag);
        _selectedTags.Sort();

        var command = AssignTagCommandHandler.CreateCommand(Context, Value.Id, tag.Id); 
        Context.Commands.Handle(command);

        await ShowTagsWhenAvailable();
        UpdateCanAdd();
    }

    private async Task UnassignTag(Tag tagToRemove)
    {
        _selectedTags.Remove(tagToRemove);
        
        var command = UnassignTagCommandHandler.CreateCommand(Context, Value.Id, tagToRemove.Id); 
        Context.Commands.Handle(command);
        UpdateCanAdd();
        await ShowTagsWhenAvailable();
    }
}