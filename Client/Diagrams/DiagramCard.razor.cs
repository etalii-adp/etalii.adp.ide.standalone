using Microsoft.AspNetCore.Components;

namespace EtAlii.Adp.Client;

public partial class DiagramCard : ComponentBase
{
    [Parameter] public required Diagram Diagram { get; init; }

    [Parameter] public EventCallback<Diagram> Edited { get; set; }

    [Parameter] public EventCallback<Diagram> Deleted { get; set; }

    [Parameter] public EventCallback<Diagram> View { get; set; }

    private async Task EditDiagram(Diagram diagram)
    {
        await Edited.InvokeAsync(diagram);
    }

    private async Task DeleteDiagram(Diagram diagram)
    {
        await Deleted.InvokeAsync(diagram);
    }

    private async Task ViewDiagram(Diagram diagram)
    {
        await View.InvokeAsync(diagram);
    }
}