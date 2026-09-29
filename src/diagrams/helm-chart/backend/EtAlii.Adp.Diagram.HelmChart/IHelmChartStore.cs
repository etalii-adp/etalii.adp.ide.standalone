namespace EtAlii.Adp.Diagram.HelmChart;

/// <summary>
/// One <see cref="HelmChart"/> per registered chart root, kept true as the folder changes (Requirement 1.4).
/// </summary>
/// <remarks>
/// <para>
/// There is deliberately no save method, and no method that takes anything to write. A store for
/// a read-only diagram type has one job: hold what the folder says, and say when that changed.
/// </para>
/// <para>
/// Reading and lifetime are separate on purpose. <see cref="GetOrLoad"/> answers a question;
/// <see cref="Acquire"/> and <see cref="Release"/> are the pair a session holds across its own
/// lifetime. Without the split, a second connection closing would tear the watcher out from
/// under the first, and that connection's diagram would go quietly stale - which is the exact
/// failure this type exists to avoid.
/// </para>
/// </remarks>
public interface IHelmChartStore
{
    /// <summary>The project for <paramref name="folder"/>, reading and watching it if this is the first ask.</summary>
    HelmChart GetOrLoad(string folder);

    /// <summary>The project for <paramref name="folder"/> if one is loaded, without loading one.</summary>
    HelmChart? Get(string folder);

    /// <summary>
    /// <see cref="GetOrLoad"/>, and a claim on the folder's lifetime. Every call must be matched
    /// by a <see cref="Release"/>.
    /// </summary>
    HelmChart Acquire(string folder);

    /// <summary>
    /// Gives up one claim. When the last one goes, the watcher is disposed and the project
    /// dropped; the next ask reads the folder afresh.
    /// </summary>
    void Release(string folder);

    /// <summary>Raised after a folder has been re-read because something in it changed.</summary>
    event EventHandler<HelmChartChangedEventArgs>? Changed;
}
