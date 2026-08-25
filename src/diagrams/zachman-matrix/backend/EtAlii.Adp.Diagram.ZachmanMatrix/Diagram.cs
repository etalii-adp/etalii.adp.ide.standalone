namespace EtAlii.Adp.Diagram.ZachmanMatrix;

/// <summary>This diagram type's identity, cataloged in docs/diagrams.md as `zachman/matrix`.</summary>
public static class Diagram
{
    public static DiagramDefinition Definition { get; } = new(
        new DiagramOrigin("zachman", "matrix"),
        "Zachman Framework matrix",
        "The Zachman grid: six questions against six perspectives, as a checklist of what is documented.");
}
