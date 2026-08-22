namespace EtAlii.Adp.Diagram.CmapConceptMap;

/// <summary>This diagram type's identity, cataloged in docs/diagrams.md as `cmap/concept-map`.</summary>
public static class Diagram
{
    public static EtAlii.Adp.Diagram.DiagramDefinition Definition { get; } = new(
        new EtAlii.Adp.Diagram.DiagramOrigin("cmap", "concept-map"),
        "Concept map (free-form network of concepts with labeled relationships)");
}
