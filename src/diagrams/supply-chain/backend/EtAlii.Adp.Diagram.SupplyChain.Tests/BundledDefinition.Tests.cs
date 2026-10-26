using System.Reflection;

namespace EtAlii.Adp.Diagram.SupplyChain.Tests;

/// <summary>
/// The DISL definition bundled from etalii-adp/etalii.adp is the bytes its provenance says it is, and it loads with no
/// diagnostic at all (supply-chain-disl-fbl task 4). Nothing derives from it yet.
/// </summary>
public sealed class BundledDefinitionTests : BundledDefinitionContract<BundledDefinitionTests>, IBundledDefinitions
{
    public static Assembly Module => typeof(Diagram).Assembly;

    public static string ModuleFolder => "supply-chain";

    public static IReadOnlyList<BundledDefinitionExpectation> Definitions { get; } =
    [
        new("supply-chain", Diagram.SupplyChain.Origin.Key, "net.etalii.adp.etalii.supply-chain")
        {
            Title = "Supply chain diagram",
            Icon = "mdi-truck-delivery-outline",
            Extension = ".supply",
            Disl = "0.3",
        },
    ];
}
