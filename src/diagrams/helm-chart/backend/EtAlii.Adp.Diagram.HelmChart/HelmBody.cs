using EtAlii.Adp.Specification.Fbl;
using EtAlii.Adp.Specification.Fbl.Plugins;
using Serilog;

using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Diagram.HelmChart;

/// <summary>
/// One chart folder, opened through the module's binding with the library's folder body
/// (<see cref="FolderBody"/>) and read by <see cref="HelmChartFolderPlugin"/> (helm-chart-disl-fbl, Requirements 3
/// and 4.5): its last reading and that reading's model (<see cref="Current"/>), and <see cref="Changed"/> once a
/// burst of changes has settled.
/// </summary>
/// <remarks>
/// <para>
/// <b>Two ways in.</b> <see cref="Open"/> is the store's: the folder stays open, watched, and read again after
/// every settled burst. <see cref="Read"/> is the validator's: one reading, after which nothing is open and
/// nothing watches, so a "Validate all" leaves no watcher behind.
/// </para>
/// <para>
/// <b>The library reads, settles and reads again; this watches.</b> The folder body opens no watcher, so an
/// opened body holds one over the folder and everything under it and hands every path it reports to
/// <see cref="FolderBody.Notify"/>, which ignores what the reading does not depend on and reads once after the
/// binding's <c>settle</c>. An overflow is a change: a watcher's <c>Error</c> is the only sign that it dropped
/// events, and which path changed is then unknowable, so the folder itself is notified and read again.
/// </para>
/// <para>
/// <b>The registration is watched as the registration</b> (Requirement 4.5, finding B12): a position written
/// to its <c>layout:</c> block raises <see cref="Changed"/> without reading the folder, so every open session
/// draws the stored position, although the registration is no file rule's file.
/// </para>
/// <para>
/// Nothing here opens a file for writing: the folder body reads shared and read-only, and the binding is
/// read-only.
/// </para>
/// </remarks>
public sealed class HelmBody : IDisposable
{
    private static readonly ILogger _logger = Log.ForContext<HelmBody>();

    private readonly string _path;
    private readonly FolderBody _body;
    private volatile HelmReading _current;

    private HelmBody(string folder, string? registration, bool watch, TimeProvider? time, HelmChartReader? reader)
    {
        _path = IoPath.GetFullPath(folder);
        Registration = registration is null ? null : IoPath.GetFullPath(registration);
        _body = FolderBody.Open(_path, HelmDefinition.Binding, new HelmChartFolderPlugin(reader ?? new HelmChartReader()), Options, Registration, time);
        _current = Reading(_body.Model);
        if (!watch) return;

        _body.Changed += OnSettled;
        FolderWatcher = Directory.Exists(_path) ? Watcher(_path) : null;
    }

    /// <summary>Raised once a burst of changes has settled: after the folder was read again, or after only the registration changed.</summary>
    public event EventHandler<FolderBodyChange>? Changed;

    /// <summary>The registration's full path, or null for a folder opened without one.</summary>
    public string? Registration { get; }

    /// <summary>The last reading and its model, built once per reading.</summary>
    public HelmReading Current => _current;

    /// <summary>The watcher, for a test that has to make it overflow; null for a one-shot read and a folder that was not there.</summary>
    internal FileSystemWatcher? FolderWatcher { get; }

    /// <summary>What the folder body is told beside the binding: the directories no file rule can select (gap 3).</summary>
    private static FblOptions Options { get; } = new() { SupplementaryEntries = HelmFolderEntries.Of };

    /// <summary>
    /// Opens <paramref name="folder"/> and keeps it open, watched and read again after every settled burst, with
    /// <paramref name="registration"/>, whether or not it exists yet, watched as the registration;
    /// <paramref name="time"/> is the clock of the settle delay, the system's unless a test passes another,
    /// and <paramref name="reader"/> the reader the plugin reads with, the module's own unless one is given.
    /// </summary>
    public static HelmBody Open(string folder, string? registration = null, TimeProvider? time = null, HelmChartReader? reader = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        return new HelmBody(folder, registration, watch: true, time, reader);
    }

    /// <summary>Reads <paramref name="folder"/> once: nothing is open, watched or pending afterwards.</summary>
    public static HelmReading Read(string folder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        using var body = new HelmBody(folder, null, watch: false, null, null);
        return body.Current;
    }

    public void Dispose()
    {
        FolderWatcher?.Dispose();
        _body.Changed -= OnSettled;
        _body.Dispose();
    }

    private static HelmReading Reading(FblModel folder) => new(folder, HelmDefinition.Model(folder));

    private void OnSettled(object? sender, FolderBodyChange change)
    {
        if (change.FolderRead)
        {
            _current = Reading(_body.Model);
        }

        Changed?.Invoke(this, change);
    }

    private FileSystemWatcher Watcher(string folder)
    {
        var watcher = new FileSystemWatcher(folder)
        {
            IncludeSubdirectories = true,
            // Content matters as much as names: editing a file changes what the diagram means without renaming anything.
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
        };
        watcher.Created += (_, e) => Notify(e.FullPath);
        watcher.Changed += (_, e) => Notify(e.FullPath);
        watcher.Deleted += (_, e) => Notify(e.FullPath);
        // Either name counts: a file renamed away is as much a change as one renamed in.
        watcher.Renamed += (_, e) =>
        {
            Notify(e.OldFullPath);
            Notify(e.FullPath);
        };
        watcher.Error += (_, e) =>
        {
            _logger.Warning(e.GetException(), "The watcher for {Path} stumbled; reading it again", folder);
            Notify(folder);
        };

        // Enabled after the handlers: a watcher live before its handlers exist raises into nothing.
        watcher.EnableRaisingEvents = true;
        return watcher;
    }

    /// <summary>Hands a path the watcher reported to the folder body. Guarded: this runs on the watcher's thread, where an exception ends the process.</summary>
    private void Notify(string path)
    {
        try
        {
            _body.Notify(path);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Taking the change to {Changed} in {Path} failed", path, _path);
        }
    }
}
