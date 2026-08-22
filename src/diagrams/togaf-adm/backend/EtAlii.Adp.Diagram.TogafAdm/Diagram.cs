namespace EtAlii.Adp.Diagram.TogafAdm;

/// <summary>This diagram type's identity, cataloged in docs/diagrams.md as `togaf/adm`.</summary>
public static class Diagram
{
    public static EtAlii.Adp.Diagram.DiagramDefinition Definition { get; } = new(
        new EtAlii.Adp.Diagram.DiagramOrigin("togaf", "adm"),
        "TOGAF ADM cycle diagram");
}
