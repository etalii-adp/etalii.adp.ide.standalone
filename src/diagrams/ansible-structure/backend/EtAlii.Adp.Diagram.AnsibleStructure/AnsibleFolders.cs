using EtAlii.Adp.Diagram.Modules;
using EtAlii.Adp.Files;

namespace EtAlii.Adp.Diagram.AnsibleStructure;

/// <summary>
/// The open <see cref="AnsibleFolder"/>s of one host, one per registration, and the claims on them: two
/// sessions on one diagram share one reading, and the folder is closed when the last of them goes.
/// </summary>
/// <remarks>
/// <para>
/// <b>Reading and lifetime are separate on purpose.</b> <see cref="GetOrLoad"/> answers a question;
/// <see cref="Acquire"/> and <see cref="Release"/> are the pair a session holds across its own lifetime. Without
/// the split, a second connection closing would close the folder under the first, and its diagram would go
/// quietly stale.
/// </para>
/// <para>
/// <b>A folder is keyed by its registration</b>, the file that made it a diagram and the one a position is
/// stored in, so the folder body watches the registration each session writes to; a folder opened without one
/// is keyed by its own path.
/// </para>
/// <para>
/// There is no method that takes anything to write: the one write of this tool is a position, through core's
/// <c>SetRegistrationLayoutCommand</c>.
/// </para>
/// <para>
/// The registry, its claims and its watches are the shared <see cref="OpenFolders{TFolder}"/>'s.
/// </para>
/// </remarks>
/// <param name="time">The clock a burst of changes settles on; the system's when none is given.</param>
/// <param name="events">
/// The project roots' event sources: a folder inside a watched project root is heard through that root's one
/// watcher rather than a watcher of its own (code-structure T280).
/// </param>
public sealed class AnsibleFolders(TimeProvider? time = null, ProjectFileEvents? events = null) : IDisposable
{
    private readonly OpenFolders<AnsibleFolder> _open = new(
        (folder, registration) => new AnsibleFolder(folder, registration, time, events),
        (opened, settled) => opened.Changed += (_, _) => settled());

    /// <summary>The open folder <paramref name="folder"/> with <paramref name="registration"/>, opening it if this is the first ask.</summary>
    public AnsibleFolder GetOrLoad(string folder, string? registration = null) => _open.GetOrLoad(folder, registration);

    /// <summary><see cref="GetOrLoad"/>, and a claim on the folder's lifetime. Every call must be matched by a <see cref="Release"/>.</summary>
    public AnsibleFolder Acquire(string folder, string? registration = null) => _open.Acquire(folder, registration);

    /// <summary>Gives up one claim. When the last one goes, the folder is closed; the next ask reads it afresh.</summary>
    public void Release(string folder, string? registration = null) => _open.Release(folder, registration);

    /// <summary>
    /// Calls <paramref name="onChanged"/> with the folder each time a burst of changes to <paramref name="folder"/>
    /// with <paramref name="registration"/> settles, until the subscription it returns is disposed; once it is
    /// disposed no call follows, and disposing it twice is harmless.
    /// </summary>
    public IDisposable Watch(string folder, string? registration, Action<AnsibleFolder> onChanged) => _open.Watch(folder, registration, onChanged);

    public void Dispose() => _open.Dispose();
}
