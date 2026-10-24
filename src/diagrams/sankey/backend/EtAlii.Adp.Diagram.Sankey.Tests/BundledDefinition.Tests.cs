using System.Reflection;

namespace EtAlii.Adp.Diagram.Sankey.Tests;

/// <summary>
/// The DISL definition bundled from etalii-adp/etalii.adp is the bytes its provenance says it is, and it loads with no
/// diagnostic at all (sankey-disl-fbl task 5). The palette, menus, rows and findings are derived from it.
/// </summary>
public sealed class BundledDefinitionTests : BundledDefinitionContract<BundledDefinitionTests>, IBundledDefinitions
{
    public static Assembly Module => typeof(Diagram).Assembly;

    public static string ModuleFolder => "sankey";

    public static IReadOnlyList<BundledDefinitionExpectation> Definitions { get; } =
    [
        new("sankey", Diagram.Sankey.Origin.Key, "net.etalii.adp.etalii.sankey")
        {
            Title = "Sankey diagram",
            Icon = "mdi-chart-sankey",
            Extension = ".skv",
            Disl = "0.3",
        },
    ];
}
