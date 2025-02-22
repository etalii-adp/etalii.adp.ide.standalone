namespace EtAlii.Adp.Client;
using Cn = CommandName;

public class RedoCommandHandler : CommandHandler<RedoCommand>
{
    private readonly HistoryManager _history;

    public override bool SendToBackend => false;
    public override bool UseInUndoRedo => false;

    public RedoCommandHandler(HistoryManager history)
    {
        _history = history;
    }

    public static Command CreateCommand(DiagramContext _) => new RedoCommand();

    public override string CommandName => Cn.Redo;

    protected override async Task Do(RedoCommand command, DiagramContext context)
    {
        await _history.TryRedo(context);
    }

    protected override Task Undo(RedoCommand command, DiagramContext context)
    {
        return Task.CompletedTask;
    }}