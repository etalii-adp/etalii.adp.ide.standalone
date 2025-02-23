using Microsoft.JSInterop;

namespace EtAlii.Adp.Client;

public class AdpThemeSwitcherJsInterop : IAsyncDisposable
{
    private readonly Lazy<Task<IJSObjectReference>> _moduleTask;

    public AdpThemeSwitcherJsInterop(IJSRuntime jsRuntime)
    {
        _moduleTask = new Lazy<Task<IJSObjectReference>>(() => jsRuntime.InvokeAsync<IJSObjectReference>("import", "./_content/Blazor.Bootstrap/blazor.bootstrap.theme-switcher.js").AsTask());
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_moduleTask.IsValueCreated)
            {
                var module = await _moduleTask.Value;
                await module.DisposeAsync();
            }
        }
        catch (JSDisconnectedException)
        {
            // do nothing
        }
    }

    public async Task InitializeAsync(DotNetObjectReference<AdpThemeSwitcher>? objRef)
    {
        var module = await _moduleTask.Value;
        await module.InvokeVoidAsync("initializeTheme", objRef);
    }

    internal Task SetAutoThemeAsync(DotNetObjectReference<AdpThemeSwitcher>? objRef) => SetThemeAsync(objRef, "system");

    internal Task SetDarkThemeAsync(DotNetObjectReference<AdpThemeSwitcher>? objRef) => SetThemeAsync(objRef, "dark");

    internal Task SetLightThemeAsync(DotNetObjectReference<AdpThemeSwitcher>? objRef) => SetThemeAsync(objRef, "light");

    internal async Task SetThemeAsync(DotNetObjectReference<AdpThemeSwitcher>? objRef, string themeName)
    {
        var module = await _moduleTask.Value;
        await module.InvokeVoidAsync("setTheme", objRef, themeName);
    }
}