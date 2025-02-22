using BlazorBootstrap;

namespace EtAlii.Adp.Client;

public class AlignNodesTopCommandHandler : RibbonCommandHandler<AlignNodesTopCommand>
{
    public override bool SendToBackend => false;
    public override bool UseInUndoRedo => false;

    
    public override IconName IconName => IconName.AlignTop;
    public override string IconTitle => "Align<br/>top";

    public override bool CanHandle(DiagramContext context) => context.SelectionType == DiagramSelection.MultipleNodes;

    public override Command[] CreateCommands(DiagramContext _) => [ new AlignNodesTopCommand() ];
    
    protected override Task Do(AlignNodesTopCommand command, DiagramContext context)
    {
        return Task.CompletedTask;
    }

    protected override Task Undo(AlignNodesTopCommand change, DiagramContext context)
    {
        return Task.CompletedTask;
    }
}