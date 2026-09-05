using System.Collections.Generic;

namespace EtAlii.Adp.Backend.Diagrams;

/// <summary>Remove the elements with these ids.</summary>
public sealed record DiagramRemoveDelta(IReadOnlyList<string> ElementIds) : DiagramDelta;
