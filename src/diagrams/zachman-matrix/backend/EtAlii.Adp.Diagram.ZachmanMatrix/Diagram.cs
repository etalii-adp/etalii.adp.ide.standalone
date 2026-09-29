using EtAlii.Adp.Documents;
namespace EtAlii.Adp.Diagram.ZachmanMatrix;

/// <summary>This diagram type's identity, cataloged in docs/tools.md as `zachman/matrix`.</summary>
public static class Diagram
{
    public static DiagramDefinition[] Definitions { get; } =
    [
        new(
            new DiagramOrigin("zachman", "matrix"),
            "Zachman Framework matrix",
            "The Zachman grid: six questions against six perspectives, as a checklist of what is documented.",
            Icon: "mdi-matrix"),
    ];
}
