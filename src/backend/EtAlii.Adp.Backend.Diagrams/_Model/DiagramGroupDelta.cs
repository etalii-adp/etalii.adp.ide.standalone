namespace EtAlii.Adp.Backend.Diagrams;

/// <summary>Fold: the group element stands for the branch whose element ids are hidden.</summary>
public sealed record DiagramGroupDelta(IReadOnlyList<string> SourceElementIds, DiagramElement GroupElement) : DiagramDelta;
