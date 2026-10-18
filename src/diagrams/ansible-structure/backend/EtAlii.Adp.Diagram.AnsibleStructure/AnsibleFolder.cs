using EtAlii.Adp.Files;
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
/// this hears the folder and everything under it (<see cref="WatchedTree"/>: through the watched project root
/// that holds it, or through a watcher of its own where none does), and hands every path it hears to
/// <see cref="FolderBody.Notify"/>, which ignores what the reading does not depend on and reads once after
/// the binding's <c>settle</c>. A path the plugin's <c>watch</c> names (a role's folders, the inventories) is
/// one it depends on, so a file added to <c>roles/x/templates/</c> reads the folder again.
/// </para>
/// <para>
/// <b>An overflow is a change.</b> A lost event is the only sign that the watcher dropped events, and which
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

    internal AnsibleFolder(string folder, string? registration, TimeProvider? time = null, ProjectFileEvents? events = null)
    {
        _path = IoPath.GetFullPath(folder);
        Registration = registration is null ? null : IoPath.GetFullPath(registration);
        _body = FolderBody.Open(_path, AnsibleDefinition.Binding, new AnsibleFolderPlugin(new AnsibleFolderListing(_path)), registration: Registration, time: time);
        _disl = AnsibleDefinition.Model(_body.Model);
        _body.Changed += OnSettled;
        Tree = Directory.Exists(_path) ? new WatchedTree(_path, Heard, events) : null;
    }

    /// <summary>Raised once a burst of changes has settled: after the folder was read again, or after only the registration changed.</summary>
    public event EventHandler<FolderBodyChange>? Changed;

    /// <summary>The registration's full path, or null for a folder opened without one.</summary>
    public string? Registration { get; }

    /// <summary>The definition's model of the last reading, built once per reading.</summary>
    public DislModel Disl => _disl;

    /// <summary>What hears the folder, for a test that has to reach its watcher; null for a folder that was not there.</summary>
    internal WatchedTree? Tree { get; }

    public void Dispose()
    {
        Tree?.Dispose();
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

    private void Heard(FileEvent heard)
    {
        switch (heard)
        {
            case { Kind: FileEventKind.Lost }:
                // Which path changed is unknowable, so the folder itself is read again.
                _logger.Warning("Events under {Path} may have been lost; reading it again", _path);
                Notify(_path);
                break;
            case { Kind: FileEventKind.Renamed, OldPath: { } oldPath }:
                // Either name counts: a file renamed away is as much a change as one renamed in.
                Notify(oldPath);
                Notify(heard.Path);
                break;
            default:
                Notify(heard.Path);
                break;
        }
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
