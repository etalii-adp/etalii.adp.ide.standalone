namespace EtAlii.Adp.Diagram.Tests.Fixtures.Ordering.Alpha;

/// <summary>Vendor sorts first, type sorts last - ordering is by vendor before type.</summary>
public static class Diagram
{
    public static DiagramDefinition[] Definitions { get; } = [new(new DiagramOrigin("alpha", "z"), "Alpha Z")];
}
