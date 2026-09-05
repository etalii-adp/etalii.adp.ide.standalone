using System;

namespace EtAlii.Adp.Backend.Diagrams;

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
