using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.CausalLoopDiagram.Tests.Parity;

/// <summary>
/// A history that runs nothing: it keeps the command a provider dispatched, so a test reads the record itself
/// rather than what running it wrote, and every document is left as it was.
/// </summary>
internal sealed class RecordingHistory : IHistoryStackStore, IHistoryStack
{
    /// <summary>The last command dispatched since <see cref="Take"/>; null for none.</summary>
    private ICommand? _recorded;

    event EventHandler<HistoryChangedEventArgs>? IHistoryStackStore.Changed
    {
        add { }
        remove { }
    }

    event EventHandler? IHistoryStack.Changed
    {
        add { }
        remove { }
    }

    public bool CanUndo => false;

    public HistoryAvailability Availability { get; } = new(false, false, 0, 0);

    /// <summary>The command dispatched since the last call, and forgets it; null for none.</summary>
    public ICommand? Take()
    {
        var recorded = _recorded;
        _recorded = null;
        return recorded;
    }

    public IHistoryStack Get(string rootPath) => this;

    public void Retain(string rootPath)
    {
    }

    public void Release(string rootPath)
    {
    }

    public Task<CommandResult> ExecuteAsync(ICommand command, CancellationToken cancellationToken = default)
    {
        _recorded = command;
        return Task.FromResult(CommandResult.Success());
    }

    public Task<CommandResult> UndoAsync(CancellationToken cancellationToken = default) => Task.FromResult(CommandResult.Failure("Nothing is recorded to undo."));

    public Task<CommandResult> RedoAsync(CancellationToken cancellationToken = default) => Task.FromResult(CommandResult.Failure("Nothing is recorded to redo."));
}
