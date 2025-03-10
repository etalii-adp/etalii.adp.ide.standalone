using BlazorBootstrap;

namespace EtAlii.Adp.Client;

public class AlignNodesRightCommandHandler : RibbonCommandHandler<AlignNodesRightCommand>
{
    public override bool SendToBackend => false;
    public override bool UseInUndoRedo => false;

    
    public override IconName IconName => IconName.AlignEnd;
    public override string IconTitle => "Align<br/>right";

    public override bool CanHandle(DiagramContext context) => context.SelectionType == DiagramSelection.MultipleNodes;

    public override Command[] CreateCommands(DiagramContext context)
    {
        var selectedViews = context.Selection
            .Cast<NodeView>()
            .ToArray();
        
        var firstView = selectedViews.First();

        var right = firstView.Node.Position.X + firstView.Size!.Width / 2f;

        return selectedViews
            .Except([firstView])
            .Select(view => new NodeMoveCommand
            {
                NodeId = view.Id,
                OldPosition = view.Position,
                NewPosition = view.Position with
                {
                    X = right - view.Size!.Width / 2f
                }
            })
            .Cast<Command>()
            .ToArray();
    }

    protected override Task Do(AlignNodesRightCommand command, DiagramContext context)
    {
        return Task.CompletedTask;
    }

    protected override Task Undo(AlignNodesRightCommand change, DiagramContext context)
    {
        return Task.CompletedTask;
    }
}