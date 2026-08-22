namespace EtAlii.Adp.Diagram.NetworkTopology;

/// <summary>This diagram type's identity, cataloged in docs/diagrams.md as `network/topology`.</summary>
public static class Diagram
{
    public static DiagramDefinition Definition { get; } = new(
        new DiagramOrigin("network", "topology"),
        "Network topology diagram");
}
