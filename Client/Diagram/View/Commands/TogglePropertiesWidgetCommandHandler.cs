using BlazorBootstrap;

namespace EtAlii.Adp.Client;

public class TogglePropertiesWidgetCommandHandler : RibbonCommandHandler<TogglePropertiesWidgetCommand>
{
    public override bool SendToBackend => false;
    public override bool UseInUndoRedo => false;

    
    public override IconName IconName => IconName.FileRuled;
    public override string IconTitle => "Properties";

    public override bool CanHandle(DiagramContext context) => true;

    public override Command[] CreateCommands(DiagramContext _) => [ new TogglePropertiesWidgetCommand() ];
    
    protected override Task Do(TogglePropertiesWidgetCommand command, DiagramContext context)
    {
        context.View.ShowProperties = !context.View.ShowProperties;
        
        return Task.CompletedTask;
    }

    protected override Task Undo(TogglePropertiesWidgetCommand change, DiagramContext context)
    {
        return Task.CompletedTask;
    }
}