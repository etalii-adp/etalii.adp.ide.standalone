using BlazorBootstrap;

namespace EtAlii.Adp.Client;

public class AlignNodesBottomCommandHandler : RibbonCommandHandler<AlignNodesBottomCommand>
{
    public override bool SendToBackend => false;
    public override bool UseInUndoRedo => false;

    
    public override IconName IconName => IconName.AlignBottom;
    public override string IconTitle => "Align<br/>bottom";

    public override bool CanHandle(DiagramContext context) => context.SelectionType == DiagramSelection.MultipleNodes;

    public override Command[] CreateCommands(DiagramContext _) => [ ];
    
    protected override Task Do(AlignNodesBottomCommand command, DiagramContext context)
    {
        return Task.CompletedTask;
    }

    protected override Task Undo(AlignNodesBottomCommand change, DiagramContext context)
    {
        return Task.CompletedTask;
    }
}