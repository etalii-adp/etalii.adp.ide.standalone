using BlazorBootstrap;

namespace EtAlii.Adp.Client;

public class AlignNodesTopCommandHandler : RibbonCommandHandler<AlignNodesTopCommand>
{
    public override bool SendToBackend => false;
    public override bool UseInUndoRedo => false;

    
    public override IconName IconName => IconName.AlignTop;
    public override string IconTitle => "Align<br/>top";

    public override bool CanHandle(DiagramContext context) => context.SelectionType == DiagramSelection.MultipleNodes;

    public override Command[] CreateCommands(DiagramContext context)
    {
        var selectedViews = context.Selection
            .Cast<NodeView>()
            .ToArray();
        
        var firstView = selectedViews.First();

        var top = firstView.Node.Position.Y - firstView.Size!.Height / 2f;

        return selectedViews
            .Except([firstView])
            .Select(view => new NodeMoveCommand
            {
                NodeId = view.Id,
                OldPosition = view.Position,
                NewPosition = view.Position with
                {
                    Y = top + view.Size!.Height / 2f
                }
            })
            .Cast<Command>()
            .ToArray();
    }

    protected override Task Do(AlignNodesTopCommand command, DiagramContext context)
    {
        return Task.CompletedTask;
    }

    protected override Task Undo(AlignNodesTopCommand change, DiagramContext context)
    {
        return Task.CompletedTask;
    }
}