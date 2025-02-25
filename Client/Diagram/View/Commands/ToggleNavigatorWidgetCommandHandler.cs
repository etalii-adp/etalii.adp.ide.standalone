using BlazorBootstrap;

namespace EtAlii.Adp.Client;

public class ToggleNavigatorWidgetCommandHandler : RibbonCommandHandler<ToggleNavigatorWidgetCommand>
{
    public override bool SendToBackend => false;
    public override bool UseInUndoRedo => false;

    
    public override IconName IconName => IconName.Map;
    public override string IconTitle => "Overview<br/>&nbsp;";

    public override bool CanHandle(DiagramContext context) => true;

    public override bool IsToggled(DiagramContext context) => context.View.ShowNavigator;

    public override Command[] CreateCommands(DiagramContext _) => [ new ToggleNavigatorWidgetCommand() ];
    
    protected override Task Do(ToggleNavigatorWidgetCommand command, DiagramContext context)
    {
        context.View.ShowNavigator = !context.View.ShowNavigator;
        context.View.Refresh();
        RaiseChanged();
        
        return Task.CompletedTask;
    }

    protected override Task Undo(ToggleNavigatorWidgetCommand change, DiagramContext context)
    {
        return Task.CompletedTask;
    }
}