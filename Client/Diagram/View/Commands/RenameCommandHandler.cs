using Blazor.Diagrams.Core.Models.Base;

namespace EtAlii.Adp.Client;
using Cn = CommandName;

public class RenameCommandHandler : ICommandHandler
{
    public string CommandName => Cn.Rename;

    public Task<Change[]> Execute(SelectableModel[] selection)
    {
        var nodeView = selection.Cast<NodeView>().Single();
        nodeView.RequestNameEdit();

        return Task.FromResult(Array.Empty<Change>());
    }
}