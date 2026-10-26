using System.Reflection;
using EtAlii.Adp.Specification.Disl;
using EtAlii.Adp.Specification.Json;

namespace EtAlii.Adp.Diagram.SupplyChain.Tests;

/// <summary>
/// The DISL definition bundled from etalii-adp/etalii.adp is the bytes its provenance says it is, and it loads with no
/// diagnostic at all (supply-chain-disl-fbl task 4), with the module's plugin functions as its providers load it.
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

    /// <summary>The definition as the module holds it, loaded with its plugin functions.</summary>
    protected override BundledDefinition Load(BundledDefinitionExpectation expected) =>
        BundledDefinition.Load(Module, expected.Name + ".dis", SupplyChainWritten.Plugins());

    /// <summary>What the runtime said as the module loaded its definition, which the module's providers derive from.</summary>
    protected override IReadOnlyList<LoadDiagnostic> DiagnosticsOf(BundledDefinition loaded) => SupplyChainDefinition.Definition.Diagnostics;
}
