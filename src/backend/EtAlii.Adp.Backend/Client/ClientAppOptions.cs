namespace EtAlii.Adp.Backend.Client;

public sealed class ClientAppOptions
{
    public const string SectionName = "Client";

    // Set only for local F5 development, where Vite still owns hot-module-reload;
    // left unset in production, where the client's static build is served directly.
    public string? DevServerUrl { get; set; }
}
