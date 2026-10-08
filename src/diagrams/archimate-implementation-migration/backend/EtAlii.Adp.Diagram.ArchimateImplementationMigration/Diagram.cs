using EtAlii.Adp.Documents;
using JetBrains.Annotations;
namespace EtAlii.Adp.Diagram.ArchimateImplementationMigration;

/// <summary>This diagram type's identity, cataloged in docs/tools.md as `archimate/implementation-migration`.</summary>
[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
public static class Diagram
{
    public static DiagramDefinition[] Definitions { get; } =
    [
        new(
            new DiagramOrigin("archimate", "implementation-migration"),
            "ArchiMate — Implementation & Migration layer",
            "The work packages, deliverables and plateaus that carry an architecture from where it is to where it should be.",
            Icon: "mdi-transit-connection-variant"),
    ];
}
