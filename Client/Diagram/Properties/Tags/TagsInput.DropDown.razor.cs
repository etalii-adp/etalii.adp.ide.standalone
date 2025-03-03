using BlazorBootstrap;

namespace EtAlii.Adp.Client;

public partial class TagsInput
{
    private readonly List<Tag> _availableTags = new();
    private Dropdown _tagOptionsDropDown = null!;
    
    private void AddExistingTag(Tag tag)
    {
        _availableTags.Remove(tag);
        AddTag(tag.Name);
    }

    private async Task OnInputFieldGotFocus()
    {
        await _tagOptionsDropDown.ShowAsync();
    }

    private async Task OnInputFieldLostFocus()
    {
        await _tagOptionsDropDown.HideAsync();
    }
}