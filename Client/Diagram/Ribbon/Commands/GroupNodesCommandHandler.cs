using BlazorBootstrap;

namespace EtAlii.Adp.Client;

public class GroupNodesCommandHandler : RibbonCommandHandler<GroupNodesCommand>
{
    public override bool SendToBackend => false;
    public override bool UseInUndoRedo => false;

    
    public override IconName IconName => IconName.FolderPlus;
    public override string IconTitle => "Group";

    public override bool CanHandle(DiagramContext context) => context.CanGroup;

    public override Command[] CreateCommands(DiagramContext _) => [ new GroupNodesCommand() ];
    
    protected override Task Do(GroupNodesCommand command, DiagramContext context)
    {
        return Task.CompletedTask;
    }

    protected override Task Undo(GroupNodesCommand change, DiagramContext context)
    {
        return Task.CompletedTask;
    }
}