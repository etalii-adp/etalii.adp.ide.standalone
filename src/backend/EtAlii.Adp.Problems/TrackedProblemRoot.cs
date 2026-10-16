using EtAlii.Adp.Documents;
using Serilog;

// EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Problems;

/// <summary>One tracked project: its subscription to the root's file events, and the burst of paths waiting to settle.</summary>
internal sealed class TrackedProblemRoot : IDisposable
{
    private static readonly ILogger _logger = Log.ForContext<TrackedProblemRoot>();

    private readonly ProblemMaintenance _owner;
    private readonly IDisposable _events;
    private readonly Lock _gate = new();
    private readonly HashSet<string> _pending = new(StringComparer.OrdinalIgnoreCase);

    public string Path { get; }

    public TrackedProblemRoot(string rootPath, ProblemMaintenance owner)
    {
        Path = rootPath;
        _owner = owner;
        // Recursive, and with content as well as names: an edit that renames nothing still
        // invalidates a verdict. The root's one watcher is shared with the hierarchy and the
        // reload bridge, and guards every subscriber; an exception here loses only the one change
        // it was applying, and says so.
        _events = owner.Events.Subscribe(rootPath, Heard);
    }

    public void Enqueue(string path)
    {
        lock (_gate)
        {
            _pending.Add(path);
        }

        // Rapid changes coalesce: the window starts over until the burst settles.
        _owner.Settling.Schedule(this);
    }

    public void Dispose()
    {
        _events.Dispose();
        _owner.Settling.Cancel(this);
        lock (_gate)
        {
            _pending.Clear();
        }
    }

    /// <summary>The burst has settled: hand everything it touched to one re-validation.</summary>
    internal void Settle()
    {
        string[] paths;
        lock (_gate)
        {
            paths = _pending.ToArray();
            _pending.Clear();
        }
        if (paths.Length == 0)
        {
            return;
        }
        _ = Task.Run(async () =>
        {
            try
            {
                await _owner.RevalidateAsync(this, paths);
            }
            catch (Exception exception)
            {
                _logger.Error(exception, "Re-validating {Count} changed files under {RootPath} failed; the next change tries again", paths.Length, Path);
            }
        });
    }

    private void Heard(FileEvent heard)
    {
        switch (heard.Kind)
        {
            case FileEventKind.Created or FileEventKind.Changed:
                _owner.OnCreatedOrChanged(this, heard.Path);
                break;
            case FileEventKind.Deleted:
                _owner.OnDeleted(this, heard.Path);
                break;
            case FileEventKind.Renamed:
                _owner.OnRenamed(this, heard.OldPath!, heard.Path);
                break;
            case FileEventKind.Lost:
                // Logged by the watcher. What was lost cannot be named, and the problems keep the
                // verdicts they have until the next change; the watcher did no more than this before.
                break;
        }
    }
}
