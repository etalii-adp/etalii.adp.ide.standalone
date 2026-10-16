using EtAlii.Adp.Specification.Disl;
using EtAlii.Adp.Specification.Fbl.Plugins;
using Serilog;

using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Diagram.AnsibleStructure;

/// <summary>
/// One registered Ansible folder, opened through the library's folder body (<see cref="FolderBody"/>) with the
/// module's binding and plugin (ansible-structure-disl-fbl, Requirements 5.2 and 6.2): the reading
/// and the definition's model of it (<see cref="Disl"/>), and <see cref="Changed"/> once a
/// burst of changes has settled.
/// </summary>
/// <remarks>
/// <para>
/// <b>The library reads, settles and reads again; this watches.</b> The folder body opens no watcher, so
/// this holds one, over the folder and everything under it, and hands every path it reports to
/// <see cref="FolderBody.Notify"/>, which ignores what the reading does not depend on and reads once after
/// the binding's <c>settle</c>. A path the plugin's <c>watch</c> names (a role's folders, the inventories) is
/// one it depends on, so a file added to <c>roles/x/templates/</c> reads the folder again.
/// </para>
/// <para>
/// <b>An overflow is a change.</b> A watcher's <c>Error</c> is the only sign that it dropped events, and which
/// path changed is then unknowable, so the folder itself is notified and read again.
/// </para>
/// <para>
/// <b>The registration is watched as the registration</b>: a write to its <c>layout:</c> block raises
/// <see cref="Changed"/> without reading the folder, so every open session draws the stored position.
/// </para>
/// <para>
/// Nothing here opens a file for writing: the folder body reads shared and read-only, and the binding is
/// read-only.
/// </para>
/// </remarks>
public sealed class AnsibleFolder : IDisposable
{
    private static readonly ILogger _logger = Log.ForContext<AnsibleFolder>();

    private readonly string _path;
    private readonly FolderBody _body;
    private volatile DislModel _disl;

    internal AnsibleFolder(string folder, string? registration, TimeProvider? time = null)
    {
        _path = IoPath.GetFullPath(folder);
        Registration = registration is null ? null : IoPath.GetFullPath(registration);
        _body = FolderBody.Open(_path, AnsibleDefinition.Binding, new AnsibleFolderPlugin(new AnsibleFolderListing(_path)), registration: Registration, time: time);
        _disl = AnsibleDefinition.Model(_body.Model);
        _body.Changed += OnSettled;
        FolderWatcher = Directory.Exists(_path) ? Watcher(_path) : null;
    }

    /// <summary>Raised once a burst of changes has settled: after the folder was read again, or after only the registration changed.</summary>
    public event EventHandler<FolderBodyChange>? Changed;

    /// <summary>The registration's full path, or null for a folder opened without one.</summary>
    public string? Registration { get; }

    /// <summary>The definition's model of the last reading, built once per reading.</summary>
    public DislModel Disl => _disl;

    /// <summary>The watcher, for a test that has to make it overflow.</summary>
    internal FileSystemWatcher? FolderWatcher { get; }

    public void Dispose()
    {
        FolderWatcher?.Dispose();
        _body.Changed -= OnSettled;
        _body.Dispose();
    }

    private void OnSettled(object? sender, FolderBodyChange change)
    {
        if (change.FolderRead)
        {
            _disl = AnsibleDefinition.Model(_body.Model);
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
