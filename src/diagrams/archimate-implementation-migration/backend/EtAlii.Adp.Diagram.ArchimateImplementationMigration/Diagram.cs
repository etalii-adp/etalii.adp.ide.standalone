namespace EtAlii.Adp.Diagram.ArchimateImplementationMigration;

/// <summary>This diagram type's identity, cataloged in docs/diagrams.md as `archimate/implementation-migration`.</summary>
public static class Diagram
{
    public static DiagramDefinition Definition { get; } = new(
        new DiagramOrigin("archimate", "implementation-migration"),
        "ArchiMate — Implementation & Migration layer");
}
