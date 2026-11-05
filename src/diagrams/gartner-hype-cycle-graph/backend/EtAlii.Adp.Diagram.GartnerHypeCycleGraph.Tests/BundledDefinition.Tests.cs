using System.Reflection;

namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph.Tests;

/// <summary>
/// The DISL definition bundled from etalii-adp/etalii.adp is the bytes its provenance says it is.
/// </summary>
public sealed class BundledDefinitionTests : BundledDefinitionContract<BundledDefinitionTests>, IBundledDefinitions
{
    public static Assembly Module => typeof(Diagram).Assembly;

    public static string ModuleFolder => "gartner-hype-cycle-graph";

    public static IReadOnlyList<BundledDefinitionExpectation> Definitions { get; } =
        [new("gartner-hype-cycle-graph", Diagram.HypeCycleGraph.Origin.Key, "net.etalii.adp.gartner.hypecycle-graph")];
}
