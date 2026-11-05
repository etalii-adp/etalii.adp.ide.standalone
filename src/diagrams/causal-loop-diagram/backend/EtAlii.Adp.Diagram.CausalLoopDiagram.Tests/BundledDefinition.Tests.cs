using System.Reflection;

namespace EtAlii.Adp.Diagram.CausalLoopDiagram.Tests;

/// <summary>
/// The DISL definition bundled from etalii-adp/etalii.adp is the bytes its provenance says it is,
/// the bytes in <c>definition/</c>, and it loads with no diagnostic (causal-loop-disl-fbl
/// Requirements 2.4 and 2.5).
/// </summary>
public sealed class BundledDefinitionTests : BundledDefinitionContract<BundledDefinitionTests>, IBundledDefinitions
{
    public static Assembly Module => typeof(Diagram).Assembly;

    public static string ModuleFolder => "causal-loop-diagram";

    public static IReadOnlyList<BundledDefinitionExpectation> Definitions { get; } =
        [new("causal-loop-diagram", Diagram.CausalLoop.Origin.Key, "net.etalii.adp.systems.causal-loop-diagram")];
}
