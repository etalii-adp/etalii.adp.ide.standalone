using Microsoft.AspNetCore.Components;

namespace EtAlii.Adp.Client;

public partial class DiagramComponent : ComponentBase
{
    [CascadingParameter] public Diagram CurrentDiagram { get; set; } = null!;
}