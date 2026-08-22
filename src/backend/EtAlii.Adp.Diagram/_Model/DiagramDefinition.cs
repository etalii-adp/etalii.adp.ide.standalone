namespace EtAlii.Adp.Diagram;

/// <summary>
/// What a diagram-type module is: its origin/notation and its display title - mirroring one row
/// of docs/diagrams.md's catalog table. Each diagram-type project exposes exactly one of these
/// through its own static <c>Diagram.Definition</c>.
/// </summary>
public sealed record DiagramDefinition(DiagramOrigin Origin, string Title);
