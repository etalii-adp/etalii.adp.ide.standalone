namespace EtAlii.Adp.Diagram.HelmChart;

/// <summary>
/// The open chart folders of one host, one <see cref="HelmBody"/> per registration, and the claims on them
/// (Requirement 1.4; helm-chart-disl-fbl, task 9). It has no save method and no method that takes anything
/// to write: the one write of this tool is a position, through core's <c>SetRegistrationLayoutCommand</c>.
/// </summary>
public interface IHelmChartStore
{
    /// <summary>The open folder <paramref name="folder"/> with <paramref name="registration"/>, opening it if this is the first ask.</summary>
    HelmBody GetOrLoad(string folder, string? registration = null);

    /// <summary><see cref="GetOrLoad"/>, and a claim on the folder's lifetime. Every call must be matched by a <see cref="Release"/>.</summary>
    HelmBody Acquire(string folder, string? registration = null);

    /// <summary>Gives up one claim. When the last one goes, the folder is closed; the next ask reads it afresh.</summary>
    void Release(string folder, string? registration = null);

    /// <summary>
    /// Calls <paramref name="onChanged"/> with the folder each time a burst of changes to <paramref name="folder"/>
    /// with <paramref name="registration"/> settles, until the subscription it returns is disposed.
    /// </summary>
    IDisposable Watch(string folder, string? registration, Action<HelmBody> onChanged);
}
