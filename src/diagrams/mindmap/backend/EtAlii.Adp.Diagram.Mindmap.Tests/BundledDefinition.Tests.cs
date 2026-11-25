using System.Reflection;

namespace EtAlii.Adp.Diagram.Mindmap.Tests;

/// <summary>
/// The DISL definition bundled from etalii-adp/etalii.adp is the bytes its provenance says it is, and it loads with this
/// runtime without a diagnostic: every expression compiles in its own context.
/// </summary>
public sealed class BundledDefinitionTests : BundledDefinitionContract<BundledDefinitionTests>, IBundledDefinitions
{
    public static Assembly Module => typeof(Diagram).Assembly;

    public static string ModuleFolder => "mindmap";

    public static IReadOnlyList<BundledDefinitionExpectation> Definitions { get; } =
        [new("mindmap", Diagram.Mindmap.Origin.Key, "net.etalii.adp.freeplane.mindmap") { Disl = "0.12" }];

    /// <summary>The definition is DISL 0.12 for links between tools (etalii.adp specs 089 and 092), of which this runtime warns; it reads what links need of 0.12, and a warning is allowed.</summary>
    protected override bool AWarningIsAllowed => true;
}
