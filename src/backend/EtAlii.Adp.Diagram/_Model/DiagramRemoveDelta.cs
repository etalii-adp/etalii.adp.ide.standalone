namespace EtAlii.Adp.Diagram;

/// <summary>Remove the elements with these ids.</summary>
public sealed record DiagramRemoveDelta(IReadOnlyList<string> ElementIds) : DiagramDelta;
