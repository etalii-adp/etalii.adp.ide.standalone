namespace EtAlii.Adp.Diagram.UmlDeployment;

/// <summary>This diagram type's identity, cataloged in docs/diagrams.md as `uml/deployment`.</summary>
public static class Diagram
{
    public static EtAlii.Adp.Diagram.DiagramDefinition Definition { get; } = new(
        new EtAlii.Adp.Diagram.DiagramOrigin("uml", "deployment"),
        "Deployment diagram");
}
