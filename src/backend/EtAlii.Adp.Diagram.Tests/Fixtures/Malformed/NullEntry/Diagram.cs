using EtAlii.Adp.Documents;
using JetBrains.Annotations;
namespace EtAlii.Adp.Diagram.Tests.Fixtures.Malformed.NullEntry;

/// <summary>
/// A good definition and a null one in the same array - a failure mode the singular property
/// could not have. The good one must survive; only the null costs an entry.
/// </summary>
[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
public static class Diagram
{
    public static DiagramDefinition[] Definitions { get; } =
    [
        new(new DiagramOrigin("fixture", "survivor"), "Survivor"),
        null!,
    ];
}
