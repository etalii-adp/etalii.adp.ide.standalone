namespace EtAlii.Adp.Diagram.TogafAdm;

/// <summary>This diagram type's identity, cataloged in docs/diagrams.md as `togaf/adm`.</summary>
public static class Diagram
{
    public static DiagramDefinition Definition { get; } = new(
        new DiagramOrigin("togaf", "adm"),
        "TOGAF ADM cycle diagram",
        "The TOGAF Architecture Development Method cycle and where a piece of work sits in it.");
}
