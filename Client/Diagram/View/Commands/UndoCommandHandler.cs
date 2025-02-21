using Blazor.Diagrams.Core.Models.Base;

namespace EtAlii.Adp.Client;
using Cn = CommandName;

public class UndoCommandHandler : ICommandHandler
{
    private readonly HistoryManager _history;

    public UndoCommandHandler(HistoryManager history)
    {
        _history = history;
    }

    public string CommandName => Cn.Undo;

    public async Task<Change[]> Execute(SelectableModel[] selection)
    {
        await _history.TryUndo();

        return [];
    }
}