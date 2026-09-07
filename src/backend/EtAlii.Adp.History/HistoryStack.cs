using EtAlii.Adp.Common;
using Serilog;

namespace EtAlii.Adp.History;

/// <summary>
/// An in-memory undo/redo stack over <see cref="ICommandDispatcher"/>.
/// </summary>
/// <remarks>
/// Every operation runs under one gate, so concurrent callers - several clients on the same
/// diagram, say - are serialized and the two sides can never be read or moved half-updated.
/// <para>
/// The undo side is bounded: past <see cref="Capacity"/> the oldest entry is dropped, so a long
/// editing session cannot grow without limit. Dropping the oldest costs the ability to undo that
/// far back, which is the usual trade and the reason the limit is generous rather than tight.
/// </para>
/// </remarks>
public sealed class HistoryStack : IHistoryStack, IDisposable
{
    public const int DefaultCapacity = 100;

    private static readonly ILogger _logger = Log.ForContext<HistoryStack>();

    private readonly ICommandDispatcher _dispatcher;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>
    /// Guards both sides together. One lock rather than two, so moving an entry between them is
    /// atomic to an outside reader - with separate locks, a caller checking <see cref="CanUndo"/>
    /// mid-undo could catch the entry belonging to neither side.
    /// </summary>
    private readonly Lock _sync = new();

    /// <summary>Most recent entry last, so trimming the oldest is a cheap removal from the front.</summary>
    private readonly LinkedList<HistoryEntry> _undo = new();

    private readonly Stack<HistoryEntry> _redo = new();

    private bool _isDisposed;

    private readonly string _rootPath;
    private readonly IContextNoticeSink? _notices;

    public HistoryStack(
        ICommandDispatcher dispatcher,
        int capacity = DefaultCapacity,
        string rootPath = "",
        IContextNoticeSink? notices = null)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);

        _dispatcher = dispatcher;
        Capacity = capacity;
        _rootPath = rootPath;
        _notices = notices;
    }

    /// <summary>The most changes the undo side will hold before the oldest starts falling off.</summary>
    public int Capacity { get; }

    public bool CanUndo => UndoCount > 0;

    public bool CanRedo => RedoCount > 0;

    public int UndoCount
    {
        get
        {
            lock (_sync)
            {
                return _undo.Count;
            }
        }
    }

    public int RedoCount
    {
        get
        {
            lock (_sync)
            {
                return _redo.Count;
            }
        }
    }

    public HistoryAvailability Availability
    {
        get
        {
            lock (_sync)
            {
                return new HistoryAvailability(_undo.Count > 0, _redo.Count > 0, _undo.Count, _redo.Count);
            }
        }
    }

    public event EventHandler? Changed;

    public async Task<CommandResult> ExecuteAsync(ICommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        CommandResult result;
        var recorded = false;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            result = await _dispatcher.DispatchAsync(command, cancellationToken).ConfigureAwait(false);

            // A rejected command changed nothing, so there is nothing to record and the redo
            // side stays valid - a failed attempt must not cost the user their redo entries.
            if (!result.IsSuccess)
            {
                _logger.Warning("{Command} was rejected: {Reason}", command.GetType().Name, result.Error);
            }
            else if (result.Warning.Length > 0)
            {
                // Succeeded, and something the user should know happened anyway - a layout
                // that could not be recorded, identities that could not be saved. Told to
                // everyone watching the project rather than answered to the one caller,
                // because the loss belongs to the document: the position is missing for
                // whoever opens it next, not only for whoever dragged it.
                _logger.Warning("{Command} succeeded with a warning: {Warning}", command.GetType().Name, result.Warning);
                if (_rootPath.Length > 0)
                {
                    _notices?.Notify(_rootPath, result.Warning);
                }
            }
            else if (result.Inverse is null)
            {
                // Succeeded but cannot be undone, so it does not belong on the stack.
                _logger.Debug("{Command} succeeded without an inverse; not recorded for undo", command.GetType().Name);
            }
            else
            {
                Record(new HistoryEntry(command, result.Inverse));
                _logger.Information("{Command} executed; {UndoCount} changes can now be undone", command.GetType().Name, UndoCount);
                recorded = true;
            }
        }
        finally
        {
            _gate.Release();
        }

        // Outside the gate: a subscriber that re-enters the store cannot deadlock the stack.
        if (recorded)
        {
            RaiseChanged();
        }

        return result;
    }

    private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);

    public async Task<CommandResult> UndoAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        CommandResult result;
        var moved = false;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            HistoryEntry entry;
            lock (_sync)
            {
                if (_undo.Last is null)
                {
                    _logger.Debug("Undo asked for with an empty undo stack");
                    return CommandResult.Failure("There is nothing to undo.");
                }

                entry = _undo.Last.Value;
            }

            result = await _dispatcher.DispatchAsync(entry.Inverse, cancellationToken).ConfigureAwait(false);

            // Left in place on failure: the state was not reversed, so the entry still describes
            // a real change and undo stays available to retry once the obstacle is gone.
            if (!result.IsSuccess)
            {
                _logger.Warning(
                    "Could not undo {Command}: {Reason}. It stays on the undo stack",
                    entry.Command.GetType().Name,
                    result.Error);
            }
            else
            {
                lock (_sync)
                {
                    _undo.RemoveLast();
                    _redo.Push(entry);
                }

                _logger.Information("Undid {Command}; {UndoCount} left to undo, {RedoCount} to redo", entry.Command.GetType().Name, UndoCount, RedoCount);
                moved = true;
            }
        }
        finally
        {
            _gate.Release();
        }

        if (moved)
        {
            RaiseChanged();
        }

        return result;
    }

    public async Task<CommandResult> RedoAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        CommandResult result;
        var moved = false;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            HistoryEntry entry;
            lock (_sync)
            {
                if (!_redo.TryPeek(out var peeked))
                {
                    _logger.Debug("Redo asked for with an empty redo stack");
                    return CommandResult.Failure("There is nothing to redo.");
                }

                entry = peeked;
            }

            result = await _dispatcher.DispatchAsync(entry.Command, cancellationToken).ConfigureAwait(false);
            if (!result.IsSuccess)
            {
                _logger.Warning(
                    "Could not redo {Command}: {Reason}. It stays on the redo stack",
                    entry.Command.GetType().Name,
                    result.Error);
            }
            else
            {
                // No trimming needed: a redo entry was on the undo side a moment ago, so moving
                // it back cannot push the count past the capacity it already respected.
                lock (_sync)
                {
                    _redo.Pop();
                    _undo.AddLast(entry);
                }

                _logger.Information("Redid {Command}; {UndoCount} left to undo, {RedoCount} to redo", entry.Command.GetType().Name, UndoCount, RedoCount);
                moved = true;
            }
        }
        finally
        {
            _gate.Release();
        }

        if (moved)
        {
            RaiseChanged();
        }

        return result;
    }

    public void Clear()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        _gate.Wait();
        try
        {
            lock (_sync)
            {
                _undo.Clear();
                _redo.Clear();
            }
        }
        finally
        {
            _gate.Release();
        }

        RaiseChanged();
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _gate.Dispose();
    }

    private void Record(HistoryEntry entry)
    {
        lock (_sync)
        {
            // Anything that was undone belonged to a future this new change replaces.
            _redo.Clear();

            _undo.AddLast(entry);
            while (_undo.Count > Capacity)
            {
                var dropped = _undo.First!.Value;
                _undo.RemoveFirst();
                // The user has quietly lost the ability to undo that far back, which nothing
                // else records.
                _logger.Debug("Dropped {Command} from the undo stack: it is full at {Capacity}", dropped.Command.GetType().Name, Capacity);
            }
        }
    }
}
