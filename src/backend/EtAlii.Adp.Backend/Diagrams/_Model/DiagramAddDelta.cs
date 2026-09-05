using System.Collections.Generic;

namespace EtAlii.Adp.Backend.Diagrams;

/// <summary>Upsert these elements: a new id is inserted, a known id replaced (grpc-core-communication Requirement 3.2).</summary>
public sealed record DiagramAddDelta(IReadOnlyList<DiagramElement> Elements) : DiagramDelta;
