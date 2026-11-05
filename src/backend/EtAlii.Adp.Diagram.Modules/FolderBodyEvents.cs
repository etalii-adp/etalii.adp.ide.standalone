using EtAlii.Adp.Files;
using EtAlii.Adp.Specification.Fbl.Plugins;
using Serilog;

namespace EtAlii.Adp.Diagram.Modules;

/// <summary>
/// What a watcher heard under a folder, handed to that folder's <see cref="FolderBody"/> as the paths it
/// names (<see cref="FolderBody.Notify"/>). The folder body opens no watcher and knows no file event, so a
/// module that keeps one open hears the folder itself (a <see cref="WatchedTree"/>) and translates here.
/// Ansible's and Helm's open folders each wrote this as their own copy (code-structure T294).
/// </summary>
/// <remarks>
/// <para>
/// <b>An overflow is a change.</b> A lost event is the only sign that the watcher dropped events, and which
/// path changed is then unknowable, so the folder itself is notified and read again.
/// </para>
/// <para>
/// <b>A rename is two changes</b>: a file renamed away is as much a change as one renamed in, so the old name
/// is notified and then the new one.
/// </para>
/// <para>
/// <b>Guarded, path by path.</b> This runs on the watcher's thread, where an exception ends the process, so a
/// path the folder body refuses is logged and the next one is still handed over.
/// </para>
/// <para>
/// It lives here rather than beside <see cref="FolderBody"/> because the FBL library references no project
/// of this host (fbl-implementation Requirement 1.3), and the file event is <c>EtAlii.Adp.Files</c>'.
/// </para>
/// </remarks>
public static class FolderBodyEvents
{
    private static readonly ILogger _logger = Log.ForContext(typeof(FolderBodyEvents));

    /// <summary>Hands <paramref name="heard"/>, heard under <paramref name="folder"/>, to <paramref name="body"/>.</summary>
    public static void Notify(FolderBody body, string folder, FileEvent heard)
    {
        ArgumentNullException.ThrowIfNull(body);
        Notify(body.Notify, folder, heard);
    }

    /// <summary>Hands <paramref name="heard"/>, heard under <paramref name="folder"/>, to <paramref name="notify"/> as the paths it names.</summary>
    internal static void Notify(Action<string> notify, string folder, FileEvent heard)
    {
        switch (heard)
        {
            case { Kind: FileEventKind.Lost }:
                // Which path changed is unknowable, so the folder itself is read again.
                _logger.Warning("Events under {Path} may have been lost; reading it again", folder);
                NotifyOne(notify, folder, folder);
                break;
            case { Kind: FileEventKind.Renamed, OldPath: { } oldPath }:
                // Either name counts: a file renamed away is as much a change as one renamed in.
                NotifyOne(notify, folder, oldPath);
                NotifyOne(notify, folder, heard.Path);
                break;
            default:
                NotifyOne(notify, folder, heard.Path);
                break;
        }
    }

    /// <summary>Hands one path to the folder body. Guarded: this runs on the watcher's thread, where an exception ends the process.</summary>
    private static void NotifyOne(Action<string> notify, string folder, string path)
    {
        try
        {
            notify(path);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Taking the change to {Changed} in {Path} failed", path, folder);
        }
    }
}
