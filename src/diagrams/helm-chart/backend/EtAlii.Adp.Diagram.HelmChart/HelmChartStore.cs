using EtAlii.Adp.Diagram.Modules;
using EtAlii.Adp.Files;

namespace EtAlii.Adp.Diagram.HelmChart;

/// <summary>
/// One <see cref="HelmBody"/> per registered chart folder, kept true as the folder changes (Requirement 1.4) -
/// the module's only reading, so two people looking at one chart cannot see different charts - and the claims
/// on it: two sessions on one diagram share one reading, and the folder is closed when the last of them goes
/// (helm-chart-disl-fbl, task 9).
/// </summary>
/// <remarks>
/// <para>
/// <b>The settling and the reading again are the library's</b> (<see cref="Specification.Fbl.Plugins.FolderBody"/>):
/// a helm dependency build rewriting <c>charts/</c> wholesale lands as one read after the binding's <c>settle</c>,
/// and the walk is milliseconds, so every settled burst reads the whole chart again.
/// </para>
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
/// The registry, its claims and its watches are the shared <see cref="OpenFolders{TFolder}"/>'s.
/// </para>
/// </remarks>
/// <param name="reader">The reader the plugin reads every folder with.</param>
/// <param name="time">The clock a burst of changes settles on; the system's when none is given.</param>
/// <param name="events">
/// The project roots' event sources: a chart folder inside a watched project root is heard through that root's
/// one watcher rather than a watcher of its own (code-structure T280).
/// </param>
public sealed class HelmChartStore(HelmChartReader reader, TimeProvider? time = null, ProjectFileEvents? events = null) : IHelmChartStore, IDisposable
{
    private readonly OpenFolders<HelmBody> _open = Folders(reader ?? throw new ArgumentNullException(nameof(reader)), time, events);

    /// <summary>A store over the module's own reader; the shape the host and most tests want.</summary>
    public HelmChartStore()
        : this(new HelmChartReader())
    {
    }

    /// <inheritdoc />
    public HelmBody GetOrLoad(string folder, string? registration = null) => _open.GetOrLoad(folder, registration);

    /// <inheritdoc />
    public HelmBody Acquire(string folder, string? registration = null) => _open.Acquire(folder, registration);

    /// <inheritdoc />
    public void Release(string folder, string? registration = null) => _open.Release(folder, registration);

    /// <inheritdoc />
    /// <remarks>Once the subscription is disposed no call follows, and disposing it twice is harmless.</remarks>
    public IDisposable Watch(string folder, string? registration, Action<HelmBody> onChanged) => _open.Watch(folder, registration, onChanged);

    public void Dispose() => _open.Dispose();

    private static OpenFolders<HelmBody> Folders(HelmChartReader reader, TimeProvider? time, ProjectFileEvents? events) =>
        new((folder, registration) => HelmBody.Open(folder, registration, time, reader, events), (opened, settled) => opened.Changed += (_, _) => settled());
}
