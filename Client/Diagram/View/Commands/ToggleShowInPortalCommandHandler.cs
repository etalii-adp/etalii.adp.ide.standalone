using BlazorBootstrap;

namespace EtAlii.Adp.Client;

public class ToggleShowInPortalCommandHandler : RibbonCommandHandler<ToggleShowInPortalCommand>
{
    public override bool SendToBackend => true;
    public override bool UseInUndoRedo => true;

    
    public override IconName IconName => IconName.Share;

    public override string IconTitle => "Show in<br />portal";
    
    public override bool CanHandle(DiagramContext context) => context.Diagram.AllowPublicAccess;

    public override bool IsToggled(DiagramContext context) => context.Diagram.ShowInPortal;

    public override Command[] CreateCommands(DiagramContext context)
    {
        return
        [
            new ToggleShowInPortalCommand
            {
                Id = context.Diagram.Id,
                NewShowInPortal = !context.Diagram.ShowInPortal,
                OldShowInPortal = context.Diagram.ShowInPortal,
            }
        ];
    }

    protected override Task Do(ToggleShowInPortalCommand command, DiagramContext context)
    {
        context.Diagram.ShowInPortal = command.NewShowInPortal;
        Update(context);
        RaiseChanged();
        return Task.CompletedTask;
    }

    protected override Task Undo(ToggleShowInPortalCommand command, DiagramContext context)
    {
        context.Diagram.ShowInPortal = !command.OldShowInPortal;
        Update(context);
        RaiseChanged();
        return Task.CompletedTask;
    }
}