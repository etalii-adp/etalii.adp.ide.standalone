namespace EtAlii.Adp.Diagram.HelmCharts;

/// <summary>
/// One chart folder, read tolerantly: everything the diagram draws comes from here.
/// </summary>
/// <remarks>
/// Every path is chart-root-relative; the validator rebases to project-relative when it
/// reports. Comparing two of these in a test goes list by list or through a rendered
/// description - record equality over the lists compares references, the recorded lesson.
/// </remarks>
/// <param name="IsChart">False when the folder holds no <c>Chart.yaml</c> at all (the empty state of Requirement 10.1).</param>
/// <param name="Metadata">The parsed <c>Chart.yaml</c> facts, null when the file is absent or unreadable.</param>
/// <param name="MetadataFailure">Why <c>Chart.yaml</c> did not parse, when it did not - the rest of the chart reads on regardless (Requirement 3.1).</param>
/// <param name="Legacy">True for <c>apiVersion: v1</c>: dependencies come from <c>requirements.yaml</c> and the whole is marked legacy Helm 2 format.</param>
/// <param name="Values">The values stack: the default <c>values.yaml</c> first, overrides after it, ordinally.</param>
/// <param name="Schema">The <c>values.schema.json</c> beside the values, when present.</param>
/// <param name="Templates">Every file under <c>templates/</c>, each with its role and its literally-scanned facts.</param>
/// <param name="Crds">The <c>crds/</c> summary, null when the folder is absent.</param>
/// <param name="Dependencies">Declared dependencies - from <c>Chart.yaml</c> for v2, from <c>requirements.yaml</c> for v1.</param>
/// <param name="Vendored">What actually sits in <c>charts/</c>: unpacked chart directories and sealed archives.</param>
/// <param name="Lock">The <c>Chart.lock</c> (or <c>requirements.lock</c>), when present.</param>
/// <param name="DependenciesFailure">Why a legacy chart's <c>requirements.yaml</c> did not parse, when it did not - v2 declarations live in <c>Chart.yaml</c>, whose failure is <paramref name="MetadataFailure"/>.</param>
public sealed record HelmChart(
    bool IsChart,
    ChartMetadata? Metadata,
    HelmYamlFailure? MetadataFailure,
    bool Legacy,
    IReadOnlyList<ValuesFile> Values,
    SchemaFile? Schema,
    IReadOnlyList<TemplateFile> Templates,
    CrdsSummary? Crds,
    IReadOnlyList<DependencyDeclaration> Dependencies,
    IReadOnlyList<VendoredEntry> Vendored,
    LockFile? Lock,
    HelmYamlFailure? DependenciesFailure = null)
{
    /// <summary>The folder had no <c>Chart.yaml</c>: a registered non-chart, not an error page.</summary>
    public static HelmChart NotAChart { get; } = new(
        false, null, null, false, [], null, [], null, [], [], null);
}
