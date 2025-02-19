using Blazor.Diagrams.Core.Models;
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

    public void Execute(SelectableModel[] selection)
    {
        var links = selection
            .OfType<LinkModel>()
            .ToArray();

        foreach (var link in links)
        {
            RemoveLink(link);
        }
        
        var nodes = selection
            .OfType<NodeView>()
            .ToArray();

        foreach (var node in nodes)
        {
            RemoveNode(node);
        }
    }

    private void RemoveNode(NodeView node)
    {
        //_changePusher.Enqueue()
    }

    private void RemoveLink(LinkModel link)
    {
        //_changePusher.Enqueue()
    }
}