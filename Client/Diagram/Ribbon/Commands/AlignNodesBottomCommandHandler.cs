using BlazorBootstrap;

namespace EtAlii.Adp.Client;

public class AlignNodesBottomCommandHandler : RibbonCommandHandler<AlignNodesBottomCommand>
{
    public override bool SendToBackend => false;
    public override bool UseInUndoRedo => false;

    
    public override IconName IconName => IconName.AlignBottom;
    public override string IconTitle => "Align<br/>bottom";

    public override bool CanHandle(DiagramContext context) => context.SelectionType == DiagramSelection.MultipleNodes;

    public override Command[] CreateCommands(DiagramContext context)
    {
        var selectedViews = context.Selection
            .Cast<NodeView>()
            .ToArray();
        
        var firstView = selectedViews.First();

        var bottom = firstView.Node.Position.Y + firstView.Size!.Height / 2f;

        return selectedViews
            .Except([firstView])
            .Select(view => new NodeMoveCommand
            {
                NodeId = view.Id,
                OldPosition = view.Position,
                NewPosition = view.Position with
                {
                    Y = bottom - view.Size!.Height / 2f
                }
            })
            .Cast<Command>()
            .ToArray();
    }

    protected override Task Do(AlignNodesBottomCommand command, DiagramContext context)
    {
        return Task.CompletedTask;
    }

    protected override Task Undo(AlignNodesBottomCommand change, DiagramContext context)
    {
        return Task.CompletedTask;
    }
}