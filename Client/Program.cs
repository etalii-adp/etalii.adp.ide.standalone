using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using EtAlii.Adp.Client;
using Microsoft.AspNetCore.Components.Authorization;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(_ => new HttpClient { BaseAddress = new Uri(builder.Configuration["API_Prefix"] ?? builder.HostEnvironment.BaseAddress) });

builder.Services.AddOptions();
builder.Services.AddAuthorizationCore();

if (LocalDebugger.IsAttached)
{
    builder.Services.AddScoped<AuthenticationStateProvider, LocalAuthenticationStateProvider>();
}
else
{
    builder.Services.AddScoped<AuthenticationStateProvider, CloudAuthenticationStateProvider>();
}

await builder.Build().RunAsync();
