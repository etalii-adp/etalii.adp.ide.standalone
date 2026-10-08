using EtAlii.Adp.Documents;
using JetBrains.Annotations;
namespace EtAlii.Adp.Diagram.Tests.Fixtures.Duplicate;

/// <summary>
/// Declares the same origin as the Valid fixture, from the same assembly. With one assembly the
/// ordinal tie-break keeps whichever was seen first; the collision itself is what gets reported.
/// </summary>
[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
public static class Diagram
{
    public static DiagramDefinition[] Definitions { get; } =
    [
        new(
            new DiagramOrigin("fixture", "valid"),
            "Duplicate Of Valid"),
    ];
}
