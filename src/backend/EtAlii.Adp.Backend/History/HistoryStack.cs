namespace EtAlii.Adp.Backend.History;

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

    public HistoryStack(ICommandDispatcher dispatcher, int capacity = DefaultCapacity)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);

        _dispatcher = dispatcher;
        Capacity = capacity;
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

    public async Task<CommandResult> ExecuteAsync(ICommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var result = await _dispatcher.DispatchAsync(command, cancellationToken).ConfigureAwait(false);

            // A rejected command changed nothing, so there is nothing to record and the redo
            // side stays valid - a failed attempt must not cost the user their redo entries.
            if (!result.IsSuccess || result.Inverse is null)
            {
                return result;
            }

            Record(new HistoryEntry(command, result.Inverse));
            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<CommandResult> UndoAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            HistoryEntry entry;
            lock (_sync)
            {
                if (_undo.Last is null)
                {
                    return CommandResult.Failure("There is nothing to undo.");
                }

                entry = _undo.Last.Value;
            }

            var result = await _dispatcher.DispatchAsync(entry.Inverse, cancellationToken).ConfigureAwait(false);

            // Left in place on failure: the state was not reversed, so the entry still describes
            // a real change and undo stays available to retry once the obstacle is gone.
            if (!result.IsSuccess)
            {
                return result;
            }

            lock (_sync)
            {
                _undo.RemoveLast();
                _redo.Push(entry);
            }

            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<CommandResult> RedoAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            HistoryEntry entry;
            lock (_sync)
            {
                if (!_redo.TryPeek(out var peeked))
                {
                    return CommandResult.Failure("There is nothing to redo.");
                }

                entry = peeked;
            }

            var result = await _dispatcher.DispatchAsync(entry.Command, cancellationToken).ConfigureAwait(false);
            if (!result.IsSuccess)
            {
                return result;
            }

            // No trimming needed: a redo entry was on the undo side a moment ago, so moving it
            // back cannot push the count past the capacity it already respected.
            lock (_sync)
            {
                _redo.Pop();
                _undo.AddLast(entry);
            }

            return result;
        }
        finally
        {
            _gate.Release();
        }
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
                _undo.RemoveFirst();
            }
        }
    }
}
