using Microsoft.AspNetCore.Components;

namespace EtAlii.Adp.Client;

public partial class DeletableTagView : ComponentBase
{
    [Parameter] public Tag Tag { get; set; } = null!;

    [Parameter] public EventCallback<Tag> Delete { get; set; }

    private async Task DeleteTag()
    {
        await Delete.InvokeAsync(Tag);
    }
}