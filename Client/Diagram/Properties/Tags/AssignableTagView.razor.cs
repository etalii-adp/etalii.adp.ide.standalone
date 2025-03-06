using Microsoft.AspNetCore.Components;

namespace EtAlii.Adp.Client;

public partial class AssignableTagView : ComponentBase
{
    [Parameter] public AssignableTag Tag { get; set; } = null!;

    [Parameter] public EventCallback<Tag> Assign { get; set; }

    private async Task AssignTag()
    {
        await Assign.InvokeAsync(Tag.Tag);
    }
}