using Blazor.Diagrams.Core.Anchors;
using Blazor.Diagrams.Core.Models.Base;

namespace EtAlii.Adp.Client;
using Cn = CommandName;

public class DeleteCommandHandler : ICommandHandler
{
    private readonly DiagramView _view;
    private readonly Diagram _diagram;

    public DeleteCommandHandler(DiagramView view, Diagram diagram)
    {
        _view = view;
        _diagram = diagram;
    }

    public string CommandName => Cn.Delete;

    public Change[] Execute(SelectableModel[] selection)
    {
        var linkChanges = selection
            .OfType<LinkView>()
            .Select(RemoveLink)
            .ToArray();

        var nodeChanges = selection
            .OfType<NodeView>()
            .SelectMany(RemoveNode)
            .ToArray();
        
        return linkChanges
            .Concat(nodeChanges)
            .OrderBy(c => c is NodeRemoveChange) // We first will remove the nodes.
            .ToArray();
    }

    private Change[] RemoveNode(NodeView node)
    {
        var changes = node.Ports
            .SelectMany(p => p.Links)
            .ToArray() // Needed to safeguard against collection modifications.
            .OfType<LinkView>()
            .Select(RemoveLink)
            .ToList();

        var change = NodeRemoveChange.Apply(_diagram, node.Id, node.Position);
        changes.Add(change);

        _view.Nodes.Remove(node);
        
        return changes.ToArray();
    }

    private Change RemoveLink(LinkView link)
    {
        var source = (PortView)((SinglePortAnchor)link.Source).Port;
        var target = (PortView)((SinglePortAnchor)link.Target).Port;

        var change = LinkRemoveChange.Apply(_diagram, link.Id, source.NodeIdentifier, source.Id, target.NodeIdentifier, target.Id);
        
        _view.Links.Remove(link);
        
        return change;
    }
}