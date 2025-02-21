using Blazor.Diagrams.Core.Models.Base;

namespace EtAlii.Adp.Client;
using Cn = CommandName;

public class RedoCommandHandler : ICommandHandler
{
    private readonly HistoryManager _history;

    public RedoCommandHandler(HistoryManager history)
    {
        _history = history;
    }

    public string CommandName => Cn.Redo;

    public async Task<Change[]> Execute(SelectableModel[] selection)
    {
        await _history.TryRedo();

        return [];
    }
}