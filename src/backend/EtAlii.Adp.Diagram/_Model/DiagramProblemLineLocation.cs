namespace EtAlii.Adp.Diagram;

/// <summary>The problem sits on one line of the document text, 1-based.</summary>
public sealed record DiagramProblemLineLocation(uint Number) : DiagramProblemLocation;
