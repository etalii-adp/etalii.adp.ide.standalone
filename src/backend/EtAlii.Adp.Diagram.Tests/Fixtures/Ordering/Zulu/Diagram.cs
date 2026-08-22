namespace EtAlii.Adp.Diagram.Tests.Fixtures.Ordering.Zulu;

/// <summary>Vendor sorts last; paired with the Alpha fixture to pin result ordering.</summary>
public static class Diagram
{
    public static DiagramDefinition Definition { get; } = new(new DiagramOrigin("zulu", "a"), "Zulu A");
}
