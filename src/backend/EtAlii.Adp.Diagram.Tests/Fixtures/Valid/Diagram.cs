using EtAlii.Adp.Documents;
using JetBrains.Annotations;
namespace EtAlii.Adp.Diagram.Tests.Fixtures.Valid;

/// <summary>The shape every real diagram-type module exposes; discovery must find this one.</summary>
[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
public static class Diagram
{
    public static DiagramDefinition[] Definitions { get; } =
    [
        new(
            new DiagramOrigin("fixture", "valid"),
            "Valid Fixture"),
    ];
}
