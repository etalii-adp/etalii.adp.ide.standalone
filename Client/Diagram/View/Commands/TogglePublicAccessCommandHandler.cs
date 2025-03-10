using BlazorBootstrap;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace EtAlii.Adp.Client;

public class TogglePublicAccessCommandHandler : RibbonCommandHandler<TogglePublicAccessCommand>
{
    public override bool SendToBackend => true;
    public override bool UseInUndoRedo => true;

    
    public override IconName IconName => _iconName;
    private IconName _iconName = IconName.Lock;

    public override string IconTitle => _iconTitle;
    private string _iconTitle = "Private<br />only";

    private readonly IJSRuntime _jsRuntime;

    private readonly NavigationManager _navigation;
    
    public TogglePublicAccessCommandHandler(IJSRuntime jsRuntime, NavigationManager navigation)
    {
        _jsRuntime = jsRuntime;
        _navigation = navigation;
    }

    public override bool CanHandle(DiagramContext context) => true;

    public override bool IsToggled(DiagramContext context) => context.Diagram.AllowPublicAccess;

    public override Command[] CreateCommands(DiagramContext context)
    {
        return
        [
            new TogglePublicAccessCommand
            {
                Id = context.Diagram.Id,
                NewAllowPublicAccess = !context.Diagram.AllowPublicAccess,
                OldAllowPublicAccess = context.Diagram.AllowPublicAccess,
            }
        ];
    }

    protected override async Task Do(TogglePublicAccessCommand command, DiagramContext context)
    {
        context.Diagram.AllowPublicAccess = command.NewAllowPublicAccess;
        Update(context);
        RaiseChanged();

        if (context.Diagram.AllowPublicAccess)
        {
            var url = _navigation.ToAbsoluteUri($"/diagram/{context.Diagram.Id}");
            await _jsRuntime.InvokeVoidAsync("navigator.clipboard.writeText", url);

            var message = new ToastMessage(ToastType.Info, "Public URL copied to the clipboard");
            context.ToastService.Notify(message);
        }
    }

    protected override Task Undo(TogglePublicAccessCommand command, DiagramContext context)
    {
        context.Diagram.AllowPublicAccess = !command.OldAllowPublicAccess;
        Update(context);
        RaiseChanged();
        return Task.CompletedTask;
    }

    public override void Update(DiagramContext context)
    {
        _iconName = context.Diagram.AllowPublicAccess ? IconName.Unlock : IconName.Lock;
        _iconTitle = context.Diagram.AllowPublicAccess ? "Public<br />access" : "Private<br />only";
    }
}