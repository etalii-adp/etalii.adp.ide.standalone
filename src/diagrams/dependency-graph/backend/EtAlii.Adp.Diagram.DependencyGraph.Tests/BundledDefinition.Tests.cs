using System.Reflection;

namespace EtAlii.Adp.Diagram.DependencyGraph.Tests;

/// <summary>
/// The DISL definition bundled from etalii-adp/etalii.adp is the bytes its provenance says it is.
/// </summary>
public sealed class BundledDefinitionTests : BundledDefinitionContract<BundledDefinitionTests>, IBundledDefinitions
{
    public static Assembly Module => typeof(Diagram).Assembly;

    public static string ModuleFolder => "dependency-graph";

    public static IReadOnlyList<BundledDefinitionExpectation> Definitions { get; } =
        [new("dependency-graph", Diagram.DependencyGraph.Origin.Key, "net.etalii.adp.generic.dependencies")];

    /// <summary>The definition is DISL 0.10 for <c>persistence.writtenId</c> alone (etalii.adp spec 088), of which this runtime warns; the module already reads ids as written itself, and a warning is allowed.</summary>
    protected override bool AWarningIsAllowed => true;
}
