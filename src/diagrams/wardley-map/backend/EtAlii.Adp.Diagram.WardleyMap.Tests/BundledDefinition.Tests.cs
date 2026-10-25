using System.Reflection;

namespace EtAlii.Adp.Diagram.WardleyMap.Tests;

/// <summary>
/// The DISL definition bundled from etalii-adp/etalii.adp is the bytes its provenance says it is, the bytes in
/// <c>definition/</c>, this module's language, and it loads with no diagnostic.
/// </summary>
public sealed class BundledDefinitionTests : BundledDefinitionContract<BundledDefinitionTests>, IBundledDefinitions
{
    public static Assembly Module => typeof(Diagram).Assembly;

    public static string ModuleFolder => "wardley-map";

    public static IReadOnlyList<BundledDefinitionExpectation> Definitions { get; } =
        [new("wardley-map", Diagram.WardleyMap.Origin.Key, "net.etalii.adp.wardley.map")
        {
            Title = Diagram.WardleyMap.Title,
            Icon = Diagram.WardleyMap.Icon,
            Extension = Diagram.DocumentExtension,
            Disl = "0.3",
        }];
}
