using BlazorBootstrap;

namespace EtAlii.Adp.Client;

public class RedoCommandHandler : RibbonCommandHandler<RedoCommand>
{
    private readonly HistoryManager _history;

    public override bool SendToBackend => false;
    public override bool UseInUndoRedo => false;

    public override IconName IconName => IconName.ArrowCounterclockwise;
    public override string IconTitle => "Redo";

    public RedoCommandHandler(HistoryManager history)
    {
        _history = history;
    }

    public override bool CanHandle(DiagramContext context) => context.History.Future > 0;

    public override Command[] CreateCommands(DiagramContext _) => [ new RedoCommand() ];
    
    protected override async Task Do(RedoCommand command, DiagramContext context)
    {
        await _history.TryRedo(context);
    }

    protected override Task Undo(RedoCommand command, DiagramContext context)
    {
        return Task.CompletedTask;
    }}