using BlazorBootstrap;

namespace EtAlii.Adp.Client;

public class UngroupNodesCommandHandler : RibbonCommandHandler<UngroupNodesCommand>
{
    public override bool SendToBackend => false;
    public override bool UseInUndoRedo => false;

    
    public override IconName IconName => IconName.FolderMinus;
    public override string IconTitle => "Ungroup";

    public override bool CanHandle(DiagramContext context) => context.CanUngroup;

    public override Command[] CreateCommands(DiagramContext _) => [ new UngroupNodesCommand() ];
    
    protected override Task Do(UngroupNodesCommand command, DiagramContext context)
    {
        return Task.CompletedTask;
    }

    protected override Task Undo(UngroupNodesCommand change, DiagramContext context)
    {
        return Task.CompletedTask;
    }
}