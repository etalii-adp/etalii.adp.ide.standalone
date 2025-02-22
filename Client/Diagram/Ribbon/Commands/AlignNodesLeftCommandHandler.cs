using BlazorBootstrap;

namespace EtAlii.Adp.Client;

public class AlignNodesLeftCommandHandler : RibbonCommandHandler<AlignNodesLeftCommand>
{
    public override bool SendToBackend => false;
    public override bool UseInUndoRedo => false;

    
    public override IconName IconName => IconName.AlignStart;
    public override string IconTitle => "Align<br/>left";

    public override bool CanHandle(DiagramContext context) => context.SelectionType == DiagramSelection.MultipleNodes;

    public override Command[] CreateCommands(DiagramContext _) => [ ];
    
    protected override Task Do(AlignNodesLeftCommand command, DiagramContext context)
    {
        return Task.CompletedTask;
    }

    protected override Task Undo(AlignNodesLeftCommand change, DiagramContext context)
    {
        return Task.CompletedTask;
    }
}