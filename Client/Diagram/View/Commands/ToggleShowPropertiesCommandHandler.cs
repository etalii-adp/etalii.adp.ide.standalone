using BlazorBootstrap;

namespace EtAlii.Adp.Client;

public class ToggleShowPropertiesCommandHandler : RibbonCommandHandler<ToggleShowPropertiesCommand>
{
    public override bool SendToBackend => true;
    public override bool UseInUndoRedo => true;

    
    public override IconName IconName => IconName.FileRuled;
    public override string IconTitle => "Properties<br/>&nbsp;";

    public override bool CanHandle(DiagramContext context) => true;

    public override bool IsToggled(DiagramContext context) => context.Diagram.ShowProperties;

    public override Command[] CreateCommands(DiagramContext context) => [ new ToggleShowPropertiesCommand
    {
        Id = context.Diagram.Id,
        NewShowProperties = !context.Diagram.ShowProperties,
        OldShowProperties = context.Diagram.ShowProperties,
    }];
    
    protected override Task Do(ToggleShowPropertiesCommand command, DiagramContext context)
    {
        context.Diagram.ShowProperties = command.NewShowProperties;
        context.View.Refresh();
        RaiseChanged();
        
        return Task.CompletedTask;
    }

    protected override Task Undo(ToggleShowPropertiesCommand command, DiagramContext context)
    {
        context.Diagram.ShowProperties = !command.OldShowProperties;
        context.View.Refresh();
        RaiseChanged();

        return Task.CompletedTask;
    }
}