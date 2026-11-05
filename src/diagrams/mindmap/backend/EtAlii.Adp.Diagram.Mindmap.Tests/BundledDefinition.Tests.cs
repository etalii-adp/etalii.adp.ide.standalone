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
        [new("mindmap", Diagram.Mindmap.Origin.Key, "net.etalii.adp.freeplane.mindmap") { Disl = "0.3" }];
}
