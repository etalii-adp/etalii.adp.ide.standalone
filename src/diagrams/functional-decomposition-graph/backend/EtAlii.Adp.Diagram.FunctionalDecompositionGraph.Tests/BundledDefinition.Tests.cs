using System.Reflection;

namespace EtAlii.Adp.Diagram.FunctionalDecompositionGraph.Tests;

/// <summary>
/// The DISL definition bundled from etalii-adp/etalii.adp is the bytes its provenance says it is,
/// the bytes in <c>definition/</c>, and it loads with no diagnostic of severity error
/// (functional-decomposition-graph-disl-fbl Requirements 2.5 and 2.6).
/// </summary>
public sealed class BundledDefinitionTests : BundledDefinitionContract<BundledDefinitionTests>, IBundledDefinitions
{
    public static Assembly Module => typeof(Diagram).Assembly;

    public static string ModuleFolder => "functional-decomposition-graph";

    public static IReadOnlyList<BundledDefinitionExpectation> Definitions { get; } =
        [new("functional-decomposition-graph", Diagram.FunctionalDecompositionGraph.Origin.Key, "net.etalii.adp.etalii.functional-decomposition-graph")];

    /// <summary>Requirement 2.6 asks for no diagnostic of severity error; a warning is allowed.</summary>
    protected override bool AWarningIsAllowed => true;
}
