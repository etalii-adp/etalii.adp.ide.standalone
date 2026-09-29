using EtAlii.Adp.Documents;
namespace EtAlii.Adp.Diagram.ArchimateMotivation;

/// <summary>This diagram type's identity, cataloged in docs/tools.md as `archimate/motivation`.</summary>
public static class Diagram
{
    public static DiagramDefinition[] Definitions { get; } =
    [
        new(
            new DiagramOrigin("archimate", "motivation"),
            "ArchiMate — Motivation layer",
            "Why a change is wanted: stakeholders, drivers, goals and the requirements they justify.",
            Icon: "mdi-target"),
    ];
}
