using BlazorBootstrap;

namespace EtAlii.Adp.Client;

public class LayersCommandHandler : RibbonCommandHandler<LayersCommand>
{
    public override bool SendToBackend => false;
    public override bool UseInUndoRedo => false;
    
    public override IconName IconName => IconName.Layers;
    public override string IconTitle => "Filter by<br/>layers";

    public override bool CanHandle(DiagramContext context) => true;
    
    public override Command[] CreateCommands(DiagramContext _) => [ new LayersCommand() ];
    
    protected override Task Do(LayersCommand command, DiagramContext context)
    {
        return Task.CompletedTask;
    }

    protected override Task Undo(LayersCommand change, DiagramContext context)
    {
        return Task.CompletedTask;
    }
}