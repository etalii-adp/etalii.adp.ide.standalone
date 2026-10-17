namespace EtAlii.Adp.History;

/// <inheritdoc cref="IHistoryStackStore" />
/// <remarks>
/// Reference-counted per project on a <see cref="RetainedRegistry{TKey, TValue}"/>, with the
/// same idle grace <c>HierarchyModelStore</c> uses: on the last release the stack is not
/// dropped at once but after a short window, so a client that reconnects within it keeps its
/// undo history (Requirement 4.2). A stack only read through <see cref="Get"/> and never
/// retained is kept: nothing claimed it, so nothing lets it go.
/// <para>
/// Keyed on the <see cref="ProjectRoot"/>, so two spellings of one folder share a stack and two
/// folders the file system tells apart do not (code-structure FR-059).
/// </para>
/// </remarks>
public sealed class HistoryStackStore : IHistoryStackStore, IDisposable
{
    private static readonly TimeSpan DefaultGrace = TimeSpan.FromSeconds(30);

    private readonly Func<ProjectRoot, RetainedHistoryStack> _create;
    private readonly RetainedRegistry<ProjectRoot, RetainedHistoryStack> _stacks;

    public HistoryStackStore(ICommandDispatcher dispatcher, TimeSpan? grace = null, ICommandNoticeSink? notices = null, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        _create = root => Attach(root.FullPath, new HistoryStack(dispatcher, HistoryStack.DefaultCapacity, root.FullPath, notices));
        _stacks = new RetainedRegistry<ProjectRoot, RetainedHistoryStack>(
            grace ?? DefaultGrace,
            (_, entry) => Detach(entry),
            time ?? TimeProvider.System);
    }

    public event EventHandler<HistoryChangedEventArgs>? Changed;

    public IHistoryStack Get(string rootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        return _stacks.GetOrAdd(ProjectRoot.From(rootPath), _create).Stack;
    }

    public void Retain(string rootPath) => _stacks.Retain(ProjectRoot.From(rootPath), _create);

    public void Release(string rootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        // Not dropped now: a reconnect within the grace re-retains and keeps its history.
        _stacks.Release(ProjectRoot.From(rootPath));
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
}
