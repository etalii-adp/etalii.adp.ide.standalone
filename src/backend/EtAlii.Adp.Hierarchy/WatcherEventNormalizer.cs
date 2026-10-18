using EtAlii.Adp.Files;
using Serilog;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Hierarchy;

/// <summary>
/// Turns what the watcher reports into the changes <see cref="HierarchyModel"/> applies: the
/// watcher's own shape is translated into a <see cref="FileEvent"/>, ADP's write transients are
/// dropped, the move that publishes one is read as the create it is, and the echo of a rename the
/// model already applied is dropped. Not thread-safe: the model calls it under its own lock, which
/// is what keeps a rename's record and its echo's check in one step.
/// </summary>
internal sealed class WatcherEventNormalizer
{
    private static readonly ILogger _logger = Log.ForContext<WatcherEventNormalizer>();

    // Renames the model has already applied directly, from a command it caused rather than
    // from the watcher (see HierarchyModel.ApplyLocalRename). The FileSystemWatcher still reports
    // the same move moments later - as one Renamed on Windows, but as a separate Delete+Create on
    // Linux, where inotify does not correlate the two halves - and that echo must not double-apply
    // or, worse, land as a Remove+Create that discards the entry's id. Each applied rename is
    // remembered briefly so its watcher echo, whatever shape it takes, is recognised and
    // dropped. The window only needs to outlast the watcher's own latency.
    private static readonly TimeSpan RenameEchoWindow = TimeSpan.FromSeconds(5);
    private readonly List<(string OldPath, string NewPath, long Ticks)> _appliedRenames = new();

    /// <summary>The watcher's own shape as a <see cref="FileEvent"/>, or null when it names no path to apply.</summary>
    public static FileEvent? Translate(WatcherChangeTypes changeType, string? oldPath, string? newPath) =>
        changeType switch
        {
            WatcherChangeTypes.Created when newPath is not null => new FileEvent(FileEventKind.Created, newPath),
            WatcherChangeTypes.Deleted when oldPath is not null => new FileEvent(FileEventKind.Deleted, oldPath),
            WatcherChangeTypes.Renamed when oldPath is not null && newPath is not null => new FileEvent(FileEventKind.Renamed, newPath, oldPath),
            WatcherChangeTypes.Changed when (newPath ?? oldPath) is { } path => new FileEvent(FileEventKind.Changed, path),
            _ => null,
        };

    /// <summary>
    /// The change a heard event stands for, or null when it stands for none: a write transient, or
    /// the echo of a rename already applied. Expired rename records are pruned here.
    /// </summary>
    public FileEvent? Normalize(FileEvent heard)
    {
        // ADP's own scratch files are not project content: AdpFileWriter writes a new diagram
        // to one of these in the destination folder and moves it into place. The scratch file
        // itself never becomes an entry - but the move that publishes the diagram is reported
        // as a rename *away* from that name, and it is the only announcement the real file
        // gets, so it is turned into the create it actually is.
        if (IsTransient(heard.Path))
        {
            _logger.Verbose("Ignoring the {Kind} watcher event for scratch file {Path}", heard.Kind, heard.Path);
            return null;
        }

        if (heard.Kind == FileEventKind.Renamed && IsTransient(heard.OldPath))
        {
            _logger.Verbose("Reading the move of scratch file {OldPath} onto {NewPath} as a create", heard.OldPath, heard.Path);
            heard = new FileEvent(FileEventKind.Created, heard.Path);
        }

        // Verbose: one line per file system event, which is far too much for normal running
        // but exactly what is wanted when the tree and disk have gone out of step.
        _logger.Verbose("Watcher event {Kind}: {OldPath} -> {Path}", heard.Kind, heard.OldPath, heard.Path);

        if (IsRenameEcho(heard))
        {
            // Already applied directly from the command that caused it; this is only the
            // watcher catching up. Dropping it is what keeps a rename's id stable on Linux.
            _logger.Verbose("Dropping the watcher echo of an already-applied rename: {Kind} {OldPath} -> {Path}", heard.Kind, heard.OldPath, heard.Path);
            return null;
        }

        return heard;
    }

    /// <summary>Records a rename the model applies, or is about to, so its watcher echo is dropped.</summary>
    public void Expect(string oldPath, string newPath)
    {
        _appliedRenames.Add((IoPath.GetFullPath(oldPath), IoPath.GetFullPath(newPath), DateTime.UtcNow.Ticks));
    }

    /// <summary>
    /// Forgets a rename recorded by <see cref="Expect"/> that did not take place, so a genuine
    /// delete of the old path is not mistaken for its echo.
    /// </summary>
    public void Withdraw(string oldPath, string newPath)
    {
        var old = IoPath.GetFullPath(oldPath);
        var fresh = IoPath.GetFullPath(newPath);
        _appliedRenames.RemoveAll(r => PathEquals(r.OldPath, old) && PathEquals(r.NewPath, fresh));
    }

    /// <summary>
    /// Whether a path is a write transient rather than project content
    /// (<see cref="AdpFileWriter.IsTransient"/>). A folder scan filters on this too, so a scratch file
    /// left behind by an interrupted write can never reappear as an entry.
    /// </summary>
    /// <remarks>
    /// The <c>File.Replace</c> backup must be transient to the model, and the reason is subtle
    /// enough to spell out: ReplaceFile announces itself as <i>Created(backup)</i>,
    /// <b>Renamed(file → backup)</b>, <i>Created(file)</i>, <i>Deleted(backup)</i> - observed verbatim
    /// from a live watcher. Taken at face value, the Renamed walks the file's entry off its path onto
    /// the backup name, the Created then mints a NEW id for the same file, and the Deleted removes the
    /// old entry under its backup name. Every open document holding the old id is orphaned - which is
    /// exactly what left a causal-loop diagram unselectable after its first variable drag, since that
    /// module stores positions in the <c>.adp</c>'s <c>layout:</c> block and so rewrites the
    /// registration in place on every move. Treating the backup name as transient makes the whole
    /// sequence a no-op: the rename onto it is ignored, so the entry never leaves its path, and the
    /// create finds the path already known.
    /// </remarks>
    public static bool IsTransient(string? path) => path is not null && AdpFileWriter.IsTransient(path);

    /// <summary>
    /// Whether an event is the echo of a rename the model already applied directly. A Renamed
    /// matches the pair; a Delete matches the old half; a Create matches the new half (the two
    /// halves the Linux watcher splits a move into).
    /// </summary>
    private bool IsRenameEcho(FileEvent heard)
    {
        if (_appliedRenames.Count == 0)
        {
            return false;
        }

        var cutoff = DateTime.UtcNow.Ticks - RenameEchoWindow.Ticks;
        _appliedRenames.RemoveAll(entry => entry.Ticks < cutoff);

        var path = IoPath.GetFullPath(heard.Path);
        return heard.Kind switch
        {
            FileEventKind.Renamed => heard.OldPath is { } oldPath && _appliedRenames.Any(r => PathEquals(r.OldPath, IoPath.GetFullPath(oldPath)) && PathEquals(r.NewPath, path)),
            FileEventKind.Deleted => _appliedRenames.Any(r => PathEquals(r.OldPath, path)),
            FileEventKind.Created => _appliedRenames.Any(r => PathEquals(r.NewPath, path)),
            _ => false,
        };
    }

    private static bool PathEquals(string? left, string? right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
