using BlazorBootstrap;

namespace EtAlii.Adp.Client;

public class AlignNodesRightCommandHandler : RibbonCommandHandler<AlignNodesRightCommand>
{
    public override bool SendToBackend => false;
    public override bool UseInUndoRedo => false;

    
    public override IconName IconName => IconName.AlignEnd;
    public override string IconTitle => "Align<br/>right";

    public override bool CanHandle(DiagramContext context) => context.SelectionType == DiagramSelection.MultipleNodes;

    public override Command[] CreateCommands(DiagramContext _) => [ new AlignNodesRightCommand() ];
    
    protected override Task Do(AlignNodesRightCommand command, DiagramContext context)
    {
        return Task.CompletedTask;
    }

    protected override Task Undo(AlignNodesRightCommand change, DiagramContext context)
    {
        return Task.CompletedTask;
    }
}