namespace EtAlii.Adp.Diagram.WardleyMap;

/// <summary>This diagram type's identity, cataloged in docs/diagrams.md as `wardley/map`.</summary>
public static class Diagram
{
    public static DiagramDefinition Definition { get; } = new(
        new DiagramOrigin("wardley", "map"),
        "Wardley Map");
}
