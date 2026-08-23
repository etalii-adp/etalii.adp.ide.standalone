namespace EtAlii.Adp.Backend.Diagrams;

/// <summary>
/// One change to a diagram, in the core add/remove/group/ungroup vocabulary but as backend
/// records rather than proto - so a module builds them without depending on the generated
/// contract, and the core service maps them to <c>Delta</c> messages in one place. Elements
/// are carried opaquely: the service knows an id, a position, a mime type and a payload it
/// never reads.
/// </summary>
public abstract record DiagramDelta
{
    /// <summary>Upsert these elements: a new id is inserted, a known id replaced (grpc-core-communication Requirement 3.2).</summary>
    public sealed record Add(IReadOnlyList<DiagramElement> Elements) : DiagramDelta;

    /// <summary>Remove the elements with these ids.</summary>
    public sealed record Remove(IReadOnlyList<string> ElementIds) : DiagramDelta;

    /// <summary>Fold: the group element stands for the branch whose element ids are hidden.</summary>
    public sealed record Group(IReadOnlyList<string> SourceElementIds, DiagramElement GroupElement) : DiagramDelta;

    /// <summary>Unfold: the branch's elements come back.</summary>
    public sealed record Ungroup(string GroupElementId, IReadOnlyList<DiagramElement> Elements) : DiagramDelta;
}

/// <summary>
/// A diagram element as the core stream carries it: identity, where the module's layout put
/// it, its mime-typed kind, and a payload the core never interprets - the module's own proto
/// message, serialized, to go into the contract's <c>Any</c>.
/// </summary>
/// <param name="Id">The element's id, the same one a selection names.</param>
/// <param name="X">Layout position.</param>
/// <param name="Y">Layout position.</param>
/// <param name="Type">Mime-style element kind, e.g. <c>freeplane/mindmap+node</c>.</param>
/// <param name="PayloadTypeUrl">The type URL for the packed payload, as a protobuf <c>Any</c> uses.</param>
/// <param name="Payload">The module's serialized payload message.</param>
public sealed record DiagramElement(
    string Id,
    double X,
    double Y,
    string Type,
    string PayloadTypeUrl,
    ReadOnlyMemory<byte> Payload);
