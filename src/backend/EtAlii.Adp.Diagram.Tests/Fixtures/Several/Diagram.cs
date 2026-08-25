namespace EtAlii.Adp.Diagram.Tests.Fixtures.Several;

/// <summary>
/// One class declaring three types - the reason the property is plural. C4 is the real case:
/// seven notations over one engine, which used to mean seven assemblies declaring one
/// definition each.
/// </summary>
public static class Diagram
{
    public static DiagramDefinition[] Definitions { get; } =
    [
        new(new DiagramOrigin("several", "first"), "Several First"),
        new(new DiagramOrigin("several", "second"), "Several Second"),
        new(new DiagramOrigin("several", "third"), "Several Third"),
    ];
}
