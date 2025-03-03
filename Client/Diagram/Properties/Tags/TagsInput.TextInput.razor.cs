using Microsoft.AspNetCore.Components.Web;

namespace EtAlii.Adp.Client;

public partial class TagsInput
{
    private async Task HandleKeyPress(KeyboardEventArgs e)
    {
        if (e.Key == "Enter" && !string.IsNullOrWhiteSpace(_newTag) && !_tags.Contains(_newTag))
        {
            AddTag(_newTag.Trim());
            await _tagOptionsDropDown.HideAsync();
        }
        else
        {
            await _tagOptionsDropDown.ShowAsync();
        }
    }
}