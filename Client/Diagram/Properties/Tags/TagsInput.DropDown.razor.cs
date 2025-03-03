using BlazorBootstrap;

namespace EtAlii.Adp.Client;

public partial class TagsInput
{
    private readonly List<Tag> _allAvailableTags = new();
    private Tag[] _visibleAvailableTags = [];
    private Dropdown _tagOptionsDropDown = null!;
    
    private void AddExistingTag(Tag tag)
    {
        _allAvailableTags.Remove(tag);
        AddTag(tag.Name);
    }

    private async Task OnInputFieldGotFocus()
    {
        UpdateVisibleTags();
        await _tagOptionsDropDown.ShowAsync();
    }

    private async Task OnInputFieldLostFocus()
    {
        await _tagOptionsDropDown.HideAsync();
    }
    
    private void UpdateVisibleTags()
    {
        _visibleAvailableTags = string.IsNullOrWhiteSpace(_newTagName)
            ? _allAvailableTags.ToArray()
            : _allAvailableTags.Where(t => t.Name.Contains(_newTagName, StringComparison.InvariantCultureIgnoreCase)).ToArray();
    }
}