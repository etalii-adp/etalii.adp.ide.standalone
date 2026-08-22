namespace EtAlii.Adp.Diagram.ErdEntityRelationship;

/// <summary>This diagram type's identity, cataloged in docs/diagrams.md as `erd/entity-relationship`.</summary>
public static class Diagram
{
    public static EtAlii.Adp.Diagram.DiagramDefinition Definition { get; } = new(
        new EtAlii.Adp.Diagram.DiagramOrigin("erd", "entity-relationship"),
        "Entity-Relationship Diagram (Chen or Crow's Foot notation)");
}
