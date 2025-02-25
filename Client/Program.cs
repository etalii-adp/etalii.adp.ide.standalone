using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using EtAlii.Adp.Client;
using Microsoft.AspNetCore.Components.Authorization;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddSingleton(_ => new HttpClient { BaseAddress = new Uri(builder.Configuration["API_Prefix"] ?? builder.HostEnvironment.BaseAddress) });

builder.Services.AddScoped<UserManager>();
builder.Services.AddSingleton<DiagramManager>();
builder.Services.AddSingleton<ChangePusher>();
builder.Services.AddSingleton<HistoryManager>();
builder.Services.AddSingleton<CommandManager>();
builder.Services.AddSingleton<NodeManager>();
builder.Services.AddSingleton<LinkManager>();
builder.Services.AddSingleton<DiagramView>();

builder.Services.AddBlazorBootstrap();
builder.Services.AddOptions();
builder.Services.AddAuthorizationCore();

builder.Services.AddSingleton<AdpThemeSwitcherJsInterop>();
    
builder.Services.AddSingleton<ICommandHandler, PanCommandHandler>();
builder.Services.AddSingleton<ICommandHandler, ZoomCommandHandler>();

builder.Services.AddSingleton<AddNodeCommandHandler>();
builder.Services.AddSingleton<ICommandHandler, AddNodeCommandHandler>();
builder.Services.AddSingleton<ICommandHandler, RemoveNodeCommandHandler>();
builder.Services.AddSingleton<ICommandHandler, RenameNodeCommandHandler>();
builder.Services.AddSingleton<ICommandHandler, MoveNodeCommandHandler>();

builder.Services.AddSingleton<ICommandHandler, AlignNodesLeftCommandHandler>();
builder.Services.AddSingleton<ICommandHandler, AlignNodesRightCommandHandler>();
builder.Services.AddSingleton<ICommandHandler, AlignNodesTopCommandHandler>();
builder.Services.AddSingleton<ICommandHandler, AlignNodesBottomCommandHandler>();
builder.Services.AddSingleton<ICommandHandler, GroupNodesCommandHandler>();
builder.Services.AddSingleton<ICommandHandler, UngroupNodesCommandHandler>();

builder.Services.AddSingleton<ICommandHandler, DeleteCommandHandler>();

builder.Services.AddSingleton<ICommandHandler, StartNodeRenameCommandHandler>();

builder.Services.AddSingleton<ICommandHandler, UndoCommandHandler>();
builder.Services.AddSingleton<ICommandHandler, RedoCommandHandler>();

builder.Services.AddSingleton<ICommandHandler, AddLinkCommandHandler>();
builder.Services.AddSingleton<ICommandHandler, RemoveLinkCommandHandler>();

builder.Services.AddSingleton<ICommandHandler, ToggleNavigatorWidgetCommandHandler>();
builder.Services.AddSingleton<ICommandHandler, ToggleFullscreenCommandHandler>();
builder.Services.AddSingleton<ICommandHandler, TogglePropertiesWidgetCommandHandler>();


if (LocalDebugger.IsAttached)
{
    builder.Services.AddScoped<AuthenticationStateProvider, LocalAuthenticationStateProvider>();
}
else
{
    builder.Services.AddScoped<AuthenticationStateProvider, CloudAuthenticationStateProvider>();
}

await builder.Build().RunAsync();
