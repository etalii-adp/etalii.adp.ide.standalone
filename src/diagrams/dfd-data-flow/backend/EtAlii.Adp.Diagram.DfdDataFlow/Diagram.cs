namespace EtAlii.Adp.Diagram.DfdDataFlow;

/// <summary>This diagram type's identity, cataloged in docs/diagrams.md as `dfd/data-flow`.</summary>
public static class Diagram
{
    public static EtAlii.Adp.Diagram.DiagramDefinition Definition { get; } = new(
        new EtAlii.Adp.Diagram.DiagramOrigin("dfd", "data-flow"),
        "Data Flow Diagram (Yourdon/DeMarco or Gane–Sarson notation)");
}
