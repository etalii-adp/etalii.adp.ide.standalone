using BlazorBootstrap;

namespace EtAlii.Adp.Client;

public partial class TagsInput
{
    private readonly List<Tag> _allAvailableTags = new();
    private AssignableTag[] _visibleAvailableTags = [];
    private Dropdown _tagOptionsDropDown = null!;
    

    private Task OnInputFieldGotFocus() => ShowTagsWhenAvailable();

    private async Task OnInputFieldLostFocus()
    {
        await Task.Delay(TimeSpan.FromMilliseconds(10));
        await _tagOptionsDropDown.HideAsync();
    }

    private async Task ShowTagsWhenAvailable()
    {
        UpdateVisibleTags();
        if (_visibleAvailableTags.Length != 0)
        {
            if (_tagOptionsDropDown != null!)
            {
                await _tagOptionsDropDown.ShowAsync();
            }
        }
        else
        {
            await _tagOptionsDropDown.HideAsync();
        }
    }
    
    private void UpdateVisibleTags()
    {
        var prefilteredTags = string.IsNullOrWhiteSpace(_newTagName)
            ? _allAvailableTags.ToArray()
            : _allAvailableTags.Where(t => t.Name.Contains(_newTagName, StringComparison.InvariantCultureIgnoreCase));
        
        _visibleAvailableTags = prefilteredTags 
                .Where(t => _selectedTags.All(st => string.Compare(st.Name, t.Name, StringComparison.InvariantCultureIgnoreCase) != 0))
                .Where(t => _selectedTags.All(st => st.Id != t.Id))
                //.DistinctBy(t => t.Id)
                .Select(t => new AssignableTag { Tag = t, TimesUsed = Context.Diagram.Nodes.SelectMany(n => n.TagGroups).Count(g => g.Tags.Contains(t)) })
                .ToArray();
         StateHasChanged();
        
        _logger.LogInformation("Filtered to {VisibleAvailableTagCount} visible available tags using '{Filter}'", _visibleAvailableTags.Length, _newTagName);
    }
}