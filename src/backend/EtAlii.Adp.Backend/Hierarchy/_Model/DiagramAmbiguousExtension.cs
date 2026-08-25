using EtAlii.Adp.Diagram;

namespace EtAlii.Adp.Backend.Hierarchy;

/// <summary>A body file whose extension more than one type declares (Requirement 2.8).</summary>
public sealed record DiagramAmbiguousExtension(string Path, string Extension, IReadOnlyList<DiagramDefinition> Claimants) : DiagramRouting;
