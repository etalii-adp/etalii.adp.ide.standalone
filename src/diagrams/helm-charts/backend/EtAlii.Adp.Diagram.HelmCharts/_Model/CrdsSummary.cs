namespace EtAlii.Adp.Diagram.HelmCharts;

/// <summary>
/// The <c>crds/</c> folder, summarized: plain untemplated YAML that Helm installs before
/// anything renders. One node in the diagram, so one summary here.
/// </summary>
/// <param name="FileCount">YAML files in the folder.</param>
/// <param name="Failures">The ones that did not parse - <c>crds/</c> is chart-owned YAML, so parse errors are reportable (Requirement 10.3).</param>
public sealed record CrdsSummary(int FileCount, IReadOnlyList<HelmYamlFailure> Failures);
