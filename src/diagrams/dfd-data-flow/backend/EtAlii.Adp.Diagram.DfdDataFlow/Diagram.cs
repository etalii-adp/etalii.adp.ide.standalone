using EtAlii.Adp.Documents;
namespace EtAlii.Adp.Diagram.DfdDataFlow;

/// <summary>This diagram type's identity, cataloged in docs/diagrams.md as `dfd/data-flow`.</summary>
public static class Diagram
{
    public static DiagramDefinition[] Definitions { get; } =
    [
        new(
            new DiagramOrigin("dfd", "data-flow"),
            "Data Flow Diagram (Yourdon/DeMarco or Gane–Sarson notation)",
            "How data moves between processes, stores and outside parties - and where it crosses a trust boundary.",
            Icon: "mdi-transfer"),
    ];
}
