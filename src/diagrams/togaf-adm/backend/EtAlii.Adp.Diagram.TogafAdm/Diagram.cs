using EtAlii.Adp.Documents;
using JetBrains.Annotations;
namespace EtAlii.Adp.Diagram.TogafAdm;

/// <summary>This diagram type's identity, cataloged in docs/tools.md as `togaf/adm`.</summary>
[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
public static class Diagram
{
    public static DiagramDefinition[] Definitions { get; } =
    [
        new(
            new DiagramOrigin("togaf", "adm"),
            "TOGAF ADM cycle diagram",
            "The TOGAF Architecture Development Method cycle and where a piece of work sits in it.",
            Icon: "mdi-sync-circle"),
    ];
}
