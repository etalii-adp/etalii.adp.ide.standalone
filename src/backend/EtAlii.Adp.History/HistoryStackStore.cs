namespace EtAlii.Adp.History;

/// <inheritdoc cref="IHistoryStackStore" />
/// <remarks>
/// Reference-counted per project on a <see cref="RetainedRegistry{TKey, TValue}"/>, with the
/// same idle grace <c>HierarchyModelStore</c> uses: on the last release the stack is not
/// dropped at once but after a short window, so a client that reconnects within it keeps its
/// undo history (Requirement 4.2). A stack only read through <see cref="Get"/> and never
/// retained is kept: nothing claimed it, so nothing lets it go.
/// </remarks>
public sealed class HistoryStackStore : IHistoryStackStore, IDisposable
{
    private static readonly TimeSpan DefaultGrace = TimeSpan.FromSeconds(30);

    private readonly Func<string, RetainedHistoryStack> _create;
    private readonly RetainedRegistry<string, RetainedHistoryStack> _stacks;

    public HistoryStackStore(ICommandDispatcher dispatcher, TimeSpan? grace = null, IContextNoticeSink? notices = null, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        _create = rootPath => Attach(rootPath, new HistoryStack(dispatcher, HistoryStack.DefaultCapacity, rootPath, notices));
        _stacks = new RetainedRegistry<string, RetainedHistoryStack>(
            grace ?? DefaultGrace,
            (_, entry) => Detach(entry),
            time ?? TimeProvider.System,
            comparer: StringComparer.OrdinalIgnoreCase);
    }

    public event EventHandler<HistoryChangedEventArgs>? Changed;

    public IHistoryStack Get(string rootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        return _stacks.GetOrAdd(Key(rootPath), _create).Stack;
    }

    public void Retain(string rootPath) => _stacks.Retain(Key(rootPath), _create);

    public void Release(string rootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        // Not dropped now: a reconnect within the grace re-retains and keeps its history.
        _stacks.Release(Key(rootPath));
    }

    public void Dispose() => _stacks.Dispose();

    private RetainedHistoryStack Attach(string rootPath, IHistoryStack stack)
    {
        var entry = new RetainedHistoryStack(rootPath, stack);
        // Aggregate every stack's Changed into the store's own event, tagged with the project.
        entry.Handler = (_, _) => Changed?.Invoke(this, new HistoryChangedEventArgs(entry.RootPath));
        stack.Changed += entry.Handler;
        return entry;
    }

    private static void Detach(RetainedHistoryStack entry)
    {
        if (entry.Handler is not null)
        {
            entry.Stack.Changed -= entry.Handler;
        }

        (entry.Stack as IDisposable)?.Dispose();
    }

    private static string Key(string rootPath) => Path.GetFullPath(rootPath);
}
