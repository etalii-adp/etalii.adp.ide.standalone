using Microsoft.AspNetCore.Components.Web;

namespace EtAlii.Adp.Client;

public partial class TagsInput
{
    private string _newTagName = string.Empty;

    private async Task HandleKeyPress(KeyboardEventArgs e)
    {
        var newTagName = _newTagName.Trim();
        if (e.Key == "Enter" && !string.IsNullOrWhiteSpace(_newTagName))
        {
            var isAlreadySelected = _selectedTags.Any(t => t.Name == newTagName);
            var tagIsInVisibleTags = _visibleAvailableTags.Any(t => t.Tag.Name == newTagName);
            if (!isAlreadySelected && !tagIsInVisibleTags)
            {
                var tag = new Tag { Id = TagIdentifier.NewIdentifier(), Name = newTagName };
                AddNewTag(tag);
                _newTagName = "";
                await _tagOptionsDropDown.HideAsync();
            }
        }
        else
        {
            await ShowTagsWhenAvailable();
        }
    }
}