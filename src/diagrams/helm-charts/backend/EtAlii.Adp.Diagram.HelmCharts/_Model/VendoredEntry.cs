namespace EtAlii.Adp.Diagram.HelmCharts;

/// <summary>
/// One thing actually sitting in <c>charts/</c>: an unpacked chart directory (read one level
/// deep) or a sealed <c>.tgz</c> archive (never unpacked, Requirement 3.3).
/// </summary>
/// <param name="EntryName">The directory name, or the archive filename without its version-and-extension tail - what dependency matching compares against.</param>
/// <param name="RelativePath">Chart-root-relative.</param>
/// <param name="Sealed">True for an archive: labeled by file name, no interior.</param>
/// <param name="ChartName">The nested chart's own declared name, null for sealed or unreadable entries.</param>
/// <param name="ChartVersion">The nested chart's own declared version, likewise.</param>
/// <param name="ChartType"><c>application</c> or <c>library</c>, from the nested chart; <c>application</c> when unknown.</param>
/// <param name="TemplateCount">Files under the nested chart's own <c>templates/</c>.</param>
/// <param name="DeeperCount">Entries in the nested chart's own <c>charts/</c> - summarized by count rather than recursed without bound (Requirement 3.4).</param>
/// <param name="Failure">Why the nested <c>Chart.yaml</c> did not read, when it did not.</param>
public sealed record VendoredEntry(
    string EntryName,
    string RelativePath,
    bool Sealed,
    string? ChartName,
    string? ChartVersion,
    string ChartType,
    int TemplateCount,
    int DeeperCount,
    HelmYamlFailure? Failure);
