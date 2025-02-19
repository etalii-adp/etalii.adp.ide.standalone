using Blazor.Diagrams.Core.Anchors;
using Blazor.Diagrams.Core.Models.Base;

namespace EtAlii.Adp.Client;
using Cn = CommandName;

public class DeleteCommandHandler : ICommandHandler
{
    private readonly DiagramView _view;
    private readonly Diagram _diagram;
    private readonly ChangePusher _changePusher;

    public DeleteCommandHandler(DiagramView view, Diagram diagram, ChangePusher changePusher)
    {
        _view = view;
        _diagram = diagram;
        _changePusher = changePusher;
    }

    public string CommandName => Cn.Delete;

    public async Task Execute(SelectableModel[] selection)
    {
        var links = selection
            .OfType<LinkView>()
            .ToArray();

        foreach (var link in links)
        {
            await RemoveLink(link);
        }
        
        var nodes = selection
            .OfType<NodeView>()
            .ToArray();

        foreach (var node in nodes)
        {
            await RemoveNode(node);
        }
    }

    private async Task RemoveNode(NodeView node)
    {
        foreach (var link in node.Links.OfType<LinkView>())
        {
            await RemoveLink(link);
        }
        
        await Task.CompletedTask;
        // await _changePusher.Enqueue(node);
        
        _view.Nodes.Remove(node);
    }

    private async Task RemoveLink(LinkView link)
    {
        var source = (PortView)((SinglePortAnchor)link.Source).Port;
        var target = (PortView)((SinglePortAnchor)link.Target).Port;

        var change = LinkRemoveChange.Apply(_diagram, link.Id, link.SourceNodeId, source.Id, link.TargetNodeId, target.Id);
        await _changePusher.Enqueue(change);
        
        _view.Links.Remove(link);
    }
}