using System.Reflection;

namespace EtAlii.Adp.Diagram.HelmChart.Tests;

/// <summary>
/// The DISL definition bundled from etalii-adp/etalii.adp is the bytes its provenance says it is,
/// and it loads with no diagnostic of severity error.
/// </summary>
public sealed class BundledDefinitionTests : BundledDefinitionContract<BundledDefinitionTests>, IBundledDefinitions
{
    public static Assembly Module => typeof(Diagram).Assembly;

    public static string ModuleFolder => "helm-chart";

    public static IReadOnlyList<BundledDefinitionExpectation> Definitions { get; } =
    [
        new("helm-chart", Diagram.HelmCharts.Origin.Key, "net.etalii.adp.helm.chart") { Title = Diagram.HelmCharts.Title, Icon = Diagram.HelmCharts.Icon },
    ];

    protected override bool AWarningIsAllowed => true;
}
