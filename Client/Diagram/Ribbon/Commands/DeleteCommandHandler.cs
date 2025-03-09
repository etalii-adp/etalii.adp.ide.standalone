using Blazor.Diagrams.Core.Anchors;
using BlazorBootstrap;

namespace EtAlii.Adp.Client;

public class DeleteCommandHandler : RibbonCommandHandler<DeleteCommand>
{
    public override bool SendToBackend => false;
    public override bool UseInUndoRedo => false;

    public override IconName IconName => IconName.Trash;
    public override string IconTitle => "Remove<br/>&nbsp;";
    public override IconColor IconColor => IconColor.Danger;

    public override bool CanHandle(DiagramContext context) => context.SelectionType != DiagramSelection.Nothing;

    public override Command[] CreateCommands(DiagramContext context) 
    {
        var linkCommands = context.Selection
            .OfType<LinkView>()
            .Select(l => CreateRemoveLinkCommand(l, context))
            .ToArray();

        var tagGroupCommands = context.Selection
            .OfType<NodeView>()
            .Select(n => new { n.Node, n.Node.TagGroups })
            .Select(m =>
            {
                var unassignTagsCommands = m.TagGroups
                    .Select(tg =>
                    {
                        return tg.Tags
                            .Select(t => UnassignTagCommandHandler.CreateCommand(context, tg.Id, t.Id))
                            .ToArray();

                    })
                    .SelectMany(m2 => m2)
                    .ToArray();
                var removeTagGroupCommands = m.TagGroups
                    .Select(tg => RemoveTagGroupCommandHandler.Create(context, m.Node.Id, tg.Id, tg.Name, tg.Mode, tg.Order))
                    .ToArray();
                
                return unassignTagsCommands
                    .Concat(removeTagGroupCommands)
                    .ToArray();
            })
            .SelectMany(m => m)
            .ToArray();
            
        var nodeCommands = context.Selection
            .OfType<NodeView>()
            .SelectMany(n => CreateRemoveNodeCommands(n, context))
            .ToArray();
        
        var commands = linkCommands
            .Concat(nodeCommands)
            .Concat(tagGroupCommands)
            .Concat([new DeleteCommand()])
            .OrderBy(c =>
            {
                return c switch
                {
                    DeleteCommand _ => 0,    
                    TagUnassignCommand => 1,
                    TagGroupRemoveCommand => 2,
                    LinkRemoveCommand => 3,
                    NodeRemoveCommand => 4,
                    _ => 99
                };
            }) 
            .ToArray();
        
        return commands;
    }
    
    protected override Task Do(DeleteCommand command, DiagramContext context)
    {
        context.View.UnselectAll(); // We want to unselect everything as the nodes and links will be deleted.

        return Task.CompletedTask;
    }

    protected override Task Undo(DeleteCommand command, DiagramContext context)
    {
        return Task.CompletedTask;
    }
    
    private static Command[] CreateRemoveNodeCommands(NodeView node, DiagramContext context)
    {
        var commands = node.Ports
            .SelectMany(p => p.Links)
            .ToArray() // Needed to safeguard against collection modifications.
            .OfType<LinkView>()
            .Select(l => CreateRemoveLinkCommand(l, context))
            .ToList();

        var command = RemoveNodeCommandHandler.Create(context, node.Id, node.Position, node.Name);
        commands.Add(command);
        
        return commands.ToArray();
    }

    private static Command CreateRemoveLinkCommand(LinkView link, DiagramContext context)
    {
        var source = (PortView)((SinglePortAnchor)link.Source).Port;
        var target = (PortView)((SinglePortAnchor)link.Target).Port;

        var command = RemoveLinkCommandHandler.CreateCommand(context, link.Id, source.NodeIdentifier, source.Id, target.NodeIdentifier, target.Id);
        
        return command;
    }
}