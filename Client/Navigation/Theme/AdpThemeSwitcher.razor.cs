using BlazorBootstrap;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace EtAlii.Adp.Client;

public partial class AdpThemeSwitcher : BlazorBootstrapComponentBase
{
    private DotNetObjectReference<AdpThemeSwitcher>? _objRef;
    
    /// <summary>
    /// Fired when the theme is changed.
    /// </summary>
    [Parameter]
    public EventCallback<string> OnThemeChanged { get; set; }

    /// <summary>
    /// Gets or sets the dropdown menu position.
    /// </summary>
    /// <remarks>
    /// Default value is <see cref="DropdownMenuPosition.Start" />.
    /// </remarks>
    [Parameter]
    public DropdownMenuPosition Position { get; set; } = DropdownMenuPosition.Start;

    [Inject] private AdpThemeSwitcherJsInterop ThemeSwitcherJsInterop { get; set; } = null!;
    
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            await ThemeSwitcherJsInterop.InitializeAsync(_objRef);
        }

        await base.OnAfterRenderAsync(firstRender);
    }

    protected override Task OnInitializedAsync()
    {
        _objRef ??= DotNetObjectReference.Create(this);

        return base.OnInitializedAsync();
    }

    [JSInvokable]
    public async Task OnThemeChangedJS(string themeName)
    {
        if (OnThemeChanged.HasDelegate)
            await OnThemeChanged.InvokeAsync(themeName);
    }

    private Task SetAutoTheme() => ThemeSwitcherJsInterop.SetAutoThemeAsync(_objRef);

    private Task SetDarkTheme() => ThemeSwitcherJsInterop.SetDarkThemeAsync(_objRef);

    private Task SetLightTheme() => ThemeSwitcherJsInterop.SetLightThemeAsync(_objRef);

    protected override string ClassNames => BuildClassNames(Class, (BootstrapClass.Dropdown, true));

    private string DropdownMenuClassNames =>
        BuildClassNames(
            (BootstrapClass.DropdownMenu, true),
            (Position.ToDropdownMenuPositionClass(), true)
        );
}