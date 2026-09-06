using EtAlii.Adp.Common;
namespace EtAlii.Adp.Diagram.Tests.Fixtures.Ordering.Zulu;

/// <summary>Vendor sorts last, type sorts first - the pair proves vendor wins.</summary>
public static class Diagram
{
    public static DiagramDefinition[] Definitions { get; } = [new(new DiagramOrigin("zulu", "a"), "Zulu A")];
}
