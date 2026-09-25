using EtAlii.Adp.Documents;
namespace EtAlii.Adp.Diagram.Tests.Fixtures.Valid;

/// <summary>The shape every real diagram-type module exposes; discovery must find this one.</summary>
public static class Diagram
{
    public static DiagramDefinition[] Definitions { get; } =
    [
        new(
            new DiagramOrigin("fixture", "valid"),
            "Valid Fixture"),
    ];
}
