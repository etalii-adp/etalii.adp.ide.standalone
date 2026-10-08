using EtAlii.Adp.Documents;
using JetBrains.Annotations;
namespace EtAlii.Adp.Diagram.ErdEntityRelationship;

/// <summary>This diagram type's identity, cataloged in docs/tools.md as `erd/entity-relationship`.</summary>
[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
public static class Diagram
{
    public static DiagramDefinition[] Definitions { get; } =
    [
        new(
            new DiagramOrigin("erd", "entity-relationship"),
            "Entity-relationship diagram",
            "Entities, their attributes and the cardinality of every relationship between them.",
            Icon: "mdi-relation-many-to-many"),
    ];
}
