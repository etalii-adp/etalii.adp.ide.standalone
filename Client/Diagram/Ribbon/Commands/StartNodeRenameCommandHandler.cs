namespace EtAlii.Adp.Client;
using Cn = CommandName;

public class StartNodeRenameCommandHandler : CommandHandler<StartNodeRenameCommand>
{
    public override string CommandName => Cn.Rename;
    
    public override bool SendToBackend => false;
    public override bool UseInUndoRedo => false;

    public static Command CreateCommand(DiagramContext _) => new StartNodeRenameCommand();
    
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