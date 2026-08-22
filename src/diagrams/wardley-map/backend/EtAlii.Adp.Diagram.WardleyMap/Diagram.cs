namespace EtAlii.Adp.Diagram.WardleyMap;

/// <summary>This diagram type's identity, cataloged in docs/diagrams.md as `wardley/map`.</summary>
public static class Diagram
{
    public static EtAlii.Adp.Diagram.DiagramDefinition Definition { get; } = new(
        new EtAlii.Adp.Diagram.DiagramOrigin("wardley", "map"),
        "Wardley Map");
}
