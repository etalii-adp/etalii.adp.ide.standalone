namespace EtAlii.Adp.Diagram;

/// <summary>Unfold: the branch's elements come back.</summary>
public sealed record DiagramUngroupDelta(string GroupElementId, IReadOnlyList<DiagramElement> Elements) : DiagramDelta;
