using Microsoft.AspNetCore.Components;

namespace EtAlii.Adp.Client;

public partial class LoadingMessage
{

    [Parameter] public string Message { get; set; } = null!;
}