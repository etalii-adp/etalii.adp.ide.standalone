using System.Collections.Concurrent;
using Serilog;

namespace EtAlii.Adp.Backend;

/// <inheritdoc cref="IHistoryStackStore" />
/// <remarks>
/// Reference-counted per project, with the same idle-timer grace <c>HierarchyModelStore</c>
/// uses: on the last release the stack is not dropped at once but after a short window, so a
/// client that reconnects within it keeps its undo history (Requirement 4.2).
/// </remarks>
public sealed class HistoryStackStore : IHistoryStackStore, IDisposable
{
    private static readonly TimeSpan DefaultGrace = TimeSpan.FromSeconds(30);
    private static readonly ILogger _logger = Log.ForContext<HistoryStackStore>();

    private readonly TimeSpan _grace;
    private readonly Func<string, IHistoryStack> _create;
    private readonly ConcurrentDictionary<string, RetainedHistoryStack> _entries = new(StringComparer.OrdinalIgnoreCase);

    public HistoryStackStore(ICommandDispatcher dispatcher, TimeSpan? grace = null, IContextNoticeSink? notices = null)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        _create = rootPath => new HistoryStack(dispatcher, HistoryStack.DefaultCapacity, rootPath, notices);
        _grace = grace ?? DefaultGrace;
    }

    public event EventHandler<HistoryChangedEventArgs>? Changed;

    public IHistoryStack Get(string rootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        return EntryFor(rootPath).Stack;
    }

    public void Retain(string rootPath)
    {
        var entry = EntryFor(rootPath);
        lock (entry.Gate)
        {
            entry.RefCount++;
            entry.EvictionTimer?.Dispose();
            entry.EvictionTimer = null;
        }
    }

    public void Release(string rootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        if (!_entries.TryGetValue(Key(rootPath), out var entry))
        {
            return;
        }

        lock (entry.Gate)
        {
            if (entry.RefCount > 0)
            {
                entry.RefCount--;
            }

            if (entry.RefCount == 0 && entry.EvictionTimer is null)
            {
                // Not dropped now: a reconnect within the grace re-retains and keeps its history.
                entry.EvictionTimer = new Timer(_ => EvictIfIdle(rootPath), null, _grace, Timeout.InfiniteTimeSpan);
            }
        }
    }

    public void Dispose()
    {
        foreach (var key in _entries.Keys.ToArray())
        {
            if (_entries.TryRemove(key, out var entry))
            {
                Detach(entry);
            }
        }
    }

    private RetainedHistoryStack EntryFor(string rootPath) =>
        _entries.GetOrAdd(Key(rootPath), key =>
        {
            var stack = _create(key);
            var entry = new RetainedHistoryStack(key, stack);
            // Aggregate every stack's Changed into the store's own event, tagged with the project.
            entry.Handler = (_, _) => Changed?.Invoke(this, new HistoryChangedEventArgs(entry.RootPath));
            stack.Changed += entry.Handler;
            return entry;
        });

    private void EvictIfIdle(string rootPath)
    {
        var key = Key(rootPath);
        if (!_entries.TryGetValue(key, out var entry))
        {
            return;
        }

        lock (entry.Gate)
        {
            if (entry.RefCount != 0)
            {
                return; // re-retained during the grace window
            }
        }

        if (_entries.TryRemove(key, out var removed))
        {
            Detach(removed);
            _logger.Debug("Dropped the history for {RootPath}; {Remaining} project histories still held", rootPath, _entries.Count);
        }
    }

    private static void Detach(RetainedHistoryStack entry)
    {
        if (entry.Handler is not null)
        {
            entry.Stack.Changed -= entry.Handler;
        }

        entry.EvictionTimer?.Dispose();
        (entry.Stack as IDisposable)?.Dispose();
    }

    private static string Key(string rootPath) => System.IO.Path.GetFullPath(rootPath);

}
