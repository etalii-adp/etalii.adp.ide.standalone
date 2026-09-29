namespace EtAlii.Adp.Diagram.HelmChart;

/// <summary>What <c>Chart.yaml</c> says about the chart itself.</summary>
/// <param name="Name">Required by Helm; null here when absent, which validation reports.</param>
/// <param name="Version">Required by Helm; null when absent; SemVer-shaped or validation warns.</param>
/// <param name="AppVersion">The packaged application version, free-form.</param>
/// <param name="ApiVersion"><c>v2</c> for Helm 3 charts, <c>v1</c> for legacy.</param>
/// <param name="ChartType"><c>application</c> (the default) or <c>library</c>.</param>
/// <param name="Description">The chart description, empty when it has none.</param>
/// <param name="Deprecated">The upstream marked it deprecated.</param>
/// <param name="Line">The 1-based line the root mapping starts on.</param>
/// <param name="VersionLine">The 1-based line of the version value, 0 when there is none - where a not-SemVer warning points.</param>
public sealed record ChartMetadata(
    string? Name,
    string? Version,
    string? AppVersion,
    string? ApiVersion,
    string ChartType,
    string Description,
    bool Deprecated,
    uint Line,
    uint VersionLine)
{
    /// <summary>Library charts render nothing themselves, so application-only findings skip them (Requirement 1.2).</summary>
    public bool IsLibrary => string.Equals(ChartType, "library", StringComparison.OrdinalIgnoreCase);
}
