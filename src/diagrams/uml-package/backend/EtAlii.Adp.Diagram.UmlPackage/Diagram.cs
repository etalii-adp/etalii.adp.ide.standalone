namespace EtAlii.Adp.Diagram.UmlPackage;

/// <summary>This diagram type's identity, cataloged in docs/diagrams.md as `uml/package`.</summary>
public static class Diagram
{
    public static EtAlii.Adp.Diagram.DiagramDefinition Definition { get; } = new(
        new EtAlii.Adp.Diagram.DiagramOrigin("uml", "package"),
        "Package diagram");
}
