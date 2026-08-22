namespace EtAlii.Adp.Diagram.NetworkTopology;

/// <summary>This diagram type's identity, cataloged in docs/diagrams.md as `network/topology`.</summary>
public static class Diagram
{
    public static EtAlii.Adp.Diagram.DiagramDefinition Definition { get; } = new(
        new EtAlii.Adp.Diagram.DiagramOrigin("network", "topology"),
        "Network topology diagram");
}
