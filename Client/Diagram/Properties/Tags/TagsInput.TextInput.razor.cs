using Microsoft.AspNetCore.Components.Web;

namespace EtAlii.Adp.Client;

public partial class TagsInput
{
    private string _newTagName = string.Empty;

    private async Task HandleKeyPress(KeyboardEventArgs e)
    {
        if (e.Key == "Enter" && !string.IsNullOrWhiteSpace(_newTagName) && !_selectedTags.Contains(_newTagName))
        {
            AddTag(_newTagName.Trim());
            await _tagOptionsDropDown.HideAsync();
        }
        else
        {
            UpdateVisibleTags();
            await _tagOptionsDropDown.ShowAsync();
        }
    }
}