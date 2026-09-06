using EtAlii.Adp.Common;
namespace EtAlii.Adp.Diagram.UmlUseCase;

/// <summary>This diagram type's identity, cataloged in docs/diagrams.md as `uml/use-case`.</summary>
public static class Diagram
{
    public static DiagramDefinition[] Definitions { get; } =
    [
        new(
            new DiagramOrigin("uml", "use-case"),
            "Use case diagram",
            "What actors want from a system, as use cases and the relationships between them.",
            Icon: "mdi-account-group-outline"),
    ];
}
