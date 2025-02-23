using BlazorBootstrap;

namespace EtAlii.Adp.Client;

public class StartNodeRenameCommandHandler : RibbonCommandHandler<StartNodeRenameCommand>
{
    public override bool SendToBackend => false;
    public override bool UseInUndoRedo => false;
    
    public override IconName IconName => IconName.CursorText;
    public override string IconTitle => "Rename<br/>&nbsp;";

    public override bool CanHandle(DiagramContext context) => context.SelectionType == DiagramSelection.SingleNode;

    public override Command[] CreateCommands(DiagramContext _) => [ new StartNodeRenameCommand() ];
    
    protected override Task Do(StartNodeRenameCommand command, DiagramContext context)
    {
        var nodeView = context.Selection.Cast<NodeView>().Single();
        nodeView.RequestNameEdit();
        
        return Task.CompletedTask;
    }

    protected override Task Undo(StartNodeRenameCommand change, DiagramContext context)
    {
        return Task.CompletedTask;
    }
}