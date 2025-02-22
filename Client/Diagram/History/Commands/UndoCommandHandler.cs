namespace EtAlii.Adp.Client;
using Cn = CommandName;

public class UndoCommandHandler : CommandHandler<UndoCommand>
{
    private readonly HistoryManager _history;

    public override bool SendToBackend => false;
    public override bool UseInUndoRedo => false;

    public override string CommandName => Cn.Undo;

    public UndoCommandHandler(HistoryManager history)
    {
        _history = history;
    }

    public static Command CreateCommand(DiagramContext _) => new UndoCommand();
    
    protected override async Task Do(UndoCommand command, DiagramContext context)
    {
        await _history.TryUndo(context);
    }

    protected override Task Undo(UndoCommand command, DiagramContext context)
    {
        return Task.CompletedTask;
    }
}