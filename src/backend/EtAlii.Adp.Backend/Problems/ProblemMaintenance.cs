using System.Collections.Concurrent;

using EtAlii.Adp.Backend.Hierarchy;

using Serilog;

using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Backend.Problems;

/// <summary>
/// Keeps the store current while a project is open without anyone asking (Requirement 5):
/// a changed diagram is re-validated alone, a removed one loses its entries, a renamed one
/// keeps them at its new path, and rapid changes coalesce into one look.
/// </summary>
/// <remarks>
/// Watches the filesystem itself rather than a <see cref="HierarchyModel"/>: hierarchy
/// models are per-connection, id-keyed and evicted when idle, while the problem set is one
/// per project - so maintenance keeps one watcher per tracked root, alive as long as the
/// store cares (design note: a deviation from the design's "subscribes to the hierarchy's
/// entry changes", which assumed a central model that does not exist).
/// </remarks>
public sealed class ProblemMaintenance : IDisposable
{
    private static readonly ILogger _logger = Log.ForContext<ProblemMaintenance>();

    /// <summary>How long a burst of changes may keep growing before one validation answers it all.</summary>
    private static readonly TimeSpan DefaultSettleDelay = TimeSpan.FromMilliseconds(500);

    private readonly IProblemStore _store;
    private readonly ProjectValidator _validator;
    private readonly DiagramFileRouter _router;
    internal readonly TimeSpan _settleDelay;
    private readonly ConcurrentDictionary<string, TrackedProblemRoot> _tracked = new(StringComparer.OrdinalIgnoreCase);

    public ProblemMaintenance(IProblemStore store, ProjectValidator validator, DiagramFileRouter router, TimeSpan? settleDelay = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(validator);
        ArgumentNullException.ThrowIfNull(router);
        _store = store;
        _validator = validator;
        _router = router;
        _settleDelay = settleDelay ?? DefaultSettleDelay;
    }

    /// <summary>Start keeping <paramref name="rootPath"/>'s set current. Idempotent; a missing root is quietly not tracked.</summary>
    public void Track(string rootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        var root = IoPath.GetFullPath(rootPath);
        if (!Directory.Exists(root))
        {
            _logger.Warning("Not tracking {RootPath}: the folder is not there", root);
            return;
        }

        _tracked.GetOrAdd(root, key =>
        {
            _logger.Information("Keeping the problems of {RootPath} current", key);
            return new TrackedProblemRoot(key, this);
        });
    }

    public void Dispose()
    {
        foreach (var root in _tracked.Values)
        {
            root.Dispose();
        }
        _tracked.Clear();
    }

    // ---- what one event means ----------------------------------------------------------

    internal void OnCreatedOrChanged(TrackedProblemRoot root, string path)
    {
        if (IsScratch(path) || Directory.Exists(path))
        {
            return; // Folders get interesting through the files inside them.
        }
        root.Enqueue(path);
    }

    internal void OnDeleted(TrackedProblemRoot root, string path)
    {
        if (IsScratch(path))
        {
            return;
        }
        // The file - or the folder and everything beneath it - is gone; so are its problems
        // (Requirement 5.2). Cheap enough to do at once, no validation involved.
        _store.Remove(root.Path, IoPath.GetRelativePath(root.Path, path));

        // A deleted body leaves its registration promising a document that is not there -
        // the one sibling whose own change event will never come.
        var registration = IoPath.ChangeExtension(path, DiagramFileName.Extension);
        if (!string.Equals(registration, path, StringComparison.OrdinalIgnoreCase) && File.Exists(registration))
        {
            root.Enqueue(registration);
        }
    }

    internal void OnRenamed(TrackedProblemRoot root, string oldPath, string newPath)
    {
        if (IsScratch(oldPath))
        {
            // AdpFileWriter publishing a new diagram: the move onto the real name is the
            // only announcement the file gets, so it is the create it actually is.
            OnCreatedOrChanged(root, newPath);
            return;
        }
        if (IsScratch(newPath))
        {
            return;
        }
        // Renamed, not changed: the problems follow the file rather than being re-reported
        // as unchecked (Requirement 5.3).
        _store.Move(root.Path, IoPath.GetRelativePath(root.Path, oldPath), IoPath.GetRelativePath(root.Path, newPath));
    }

    internal async Task RevalidateAsync(TrackedProblemRoot root, IReadOnlyList<string> paths)
    {
        foreach (var path in paths)
        {
            if (!File.Exists(path))
            {
                continue; // Deleted since; OnDeleted already spoke.
            }

            var relative = IoPath.GetRelativePath(root.Path, path);

            // What a validation of this file speaks for - the file, and the pair it belongs
            // to, since a body's problems live on its registration file. Computed from the
            // routing, not from what was found: a pair that just became clean must still
            // clear its old entries.
            var covered = new List<string> { relative };
            switch (_router.Route(path))
            {
                case DiagramRouted routed:
                    if (routed.RegistrationPath is not null)
                    {
                        covered.Add(IoPath.GetRelativePath(root.Path, routed.RegistrationPath));
                    }
                    covered.Add(IoPath.GetRelativePath(root.Path, routed.BodyPath));
                    break;

                case NotADiagram when !HasProblemsFor(root.Path, relative):
                    continue; // Not a diagram and never was one: nothing to say, nothing to clear.
            }

            // One file changed, one file's worth of work (Requirement 5.1): a File scope
            // routes and validates just this pair.
            var outcome = await _validator.ValidateAsync(new FileValidationScope(root.Path, relative), CancellationToken.None);
            covered.AddRange(outcome.Problems.Select(problem => problem.RelativePath));
            _store.ReplaceFor(root.Path, covered.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), outcome.Problems);
        }
    }

    private bool HasProblemsFor(string rootPath, string relativePath) =>
        _store.Get(rootPath).Problems.Any(problem => string.Equals(problem.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase));

    private static bool IsScratch(string path)
    {
        var name = IoPath.GetFileName(path);
        return name.StartsWith(AdpFileWriter.TempPrefix, StringComparison.Ordinal)
               && name.EndsWith(AdpFileWriter.TempExtension, StringComparison.OrdinalIgnoreCase);
    }

}
