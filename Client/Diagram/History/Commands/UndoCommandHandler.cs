using BlazorBootstrap;

namespace EtAlii.Adp.Client;

public class UndoCommandHandler : RibbonCommandHandler<UndoCommand>
{
    private readonly HistoryManager _history;

    public override bool SendToBackend => false;
    public override bool UseInUndoRedo => false;

    public override IconName IconName => IconName.ArrowClockwise;
    public override string IconTitle => "Undo<br/>&nbsp;";

    public UndoCommandHandler(HistoryManager history)
    {
        _history = history;
    }

    public override bool CanHandle(DiagramContext context) => context.History.History > 0;

    public override Command[] CreateCommands(DiagramContext _) => [ new UndoCommand() ];
    
    protected override async Task Do(UndoCommand command, DiagramContext context)
    {
        await _history.TryUndo(context);
    }

    protected override Task Undo(UndoCommand command, DiagramContext context)
    {
        return Task.CompletedTask;
    }
}