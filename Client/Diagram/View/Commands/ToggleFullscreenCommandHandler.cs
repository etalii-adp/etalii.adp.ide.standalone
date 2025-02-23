using BlazorBootstrap;
using Microsoft.JSInterop;

namespace EtAlii.Adp.Client;

public class ToggleFullscreenCommandHandler : RibbonCommandHandler<ToggleFullscreenCommand>
{
    private readonly IJSRuntime _jsRuntime;
    public override bool SendToBackend => false;
    public override bool UseInUndoRedo => false;

    
    public override IconName IconName => _iconName;
    private IconName _iconName = IconName.Fullscreen;
    public override string IconTitle => _iconTitle;
    private string _iconTitle = "Fullscreen<br/>&nbsp;";
    public override bool CanHandle(DiagramContext context) => true;

    private bool _isFullscreen;
    public override bool IsToggled(DiagramContext context) => _isFullscreen;

    public override Command[] CreateCommands(DiagramContext _) => [ new ToggleFullscreenCommand() ];

    public ToggleFullscreenCommandHandler(IJSRuntime jsRuntime)
    {
        _jsRuntime = jsRuntime;
    }
    protected override async Task Do(ToggleFullscreenCommand command, DiagramContext context)
    {
        _isFullscreen = !_isFullscreen;
        await _jsRuntime.InvokeVoidAsync("toggleFullScreen");

        _iconName = IconName == IconName.Fullscreen 
            ? IconName.FullscreenExit
            : IconName.Fullscreen;
        
        _iconTitle = IconName == IconName.Fullscreen ? "Fullscreen" : "Exit<br/>fullscreen";
        
        RaiseChanged();
    }

    protected override Task Undo(ToggleFullscreenCommand change, DiagramContext context)
    {
        return Task.CompletedTask;
    }
}