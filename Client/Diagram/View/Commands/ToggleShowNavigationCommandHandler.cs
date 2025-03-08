using BlazorBootstrap;

namespace EtAlii.Adp.Client;

public class ToggleShowNavigationCommandHandler : RibbonCommandHandler<ToggleShowNavigationCommand>
{
    public override bool SendToBackend => true;
    public override bool UseInUndoRedo => true;

    
    public override IconName IconName => IconName.Map;
    public override string IconTitle => "Overview<br/>&nbsp;";

    public override bool CanHandle(DiagramContext context) => true;

    public override bool IsToggled(DiagramContext context) => context.Diagram.ShowNavigation;

    public override Command[] CreateCommands(DiagramContext context) => [ new ToggleShowNavigationCommand
    {
        Id = context.Diagram.Id,
        NewShowNavigation = !context.Diagram.ShowNavigation,
        OldShowNavigation = context.Diagram.ShowNavigation,
    }];
    
    protected override Task Do(ToggleShowNavigationCommand command, DiagramContext context)
    {
        context.Diagram.ShowNavigation = command.NewShowNavigation;
        context.View.Refresh();
        RaiseChanged();
        
        return Task.CompletedTask;
    }

    protected override Task Undo(ToggleShowNavigationCommand command, DiagramContext context)
    {
        context.Diagram.ShowNavigation = !command.OldShowNavigation;
        context.View.Refresh();
        RaiseChanged();

        return Task.CompletedTask;
    }
}