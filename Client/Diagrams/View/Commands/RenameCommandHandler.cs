using Blazor.Diagrams.Core.Models.Base;

namespace EtAlii.Adp.Client;
using Cn = CommandName;

public class RenameCommandHandler : ICommandHandler
{
    private readonly DiagramView _view;
    private readonly Diagram _diagram;

    public RenameCommandHandler(DiagramView view, Diagram diagram)
    {
        _view = view;
        _diagram = diagram;
    }

    public string CommandName => Cn.Rename;

    public Change[] Execute(SelectableModel[] selection)
    {
        var nodeView = selection.Cast<NodeView>().Single();
        nodeView.RequestNameEdit();

        return [];
    }
}