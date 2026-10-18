using EtAlii.Adp.Diagram.Wire;
using Grpc.Core;

namespace EtAlii.Adp.Workspace;

/// <summary>
/// How one kind of stream rides a <see cref="WorkspaceConnection"/>: what its pump writes, and how its
/// endings read on the wire. The connection owns the rest - starting it, stopping it, telling the
/// client it ended and forgetting it (code-structure FR-069).
/// </summary>
public sealed class StreamMap<TSession>
{
    /// <summary>What the log calls the stream: "Diagram", "Table".</summary>
    public required string Kind { get; init; }

    /// <summary>
    /// Writes the session's content onto the connection under the stream id until the token ends it,
    /// and disposes the session when it returns.
    /// </summary>
    public required Func<TSession, WorkspaceConnection, Documents.Wire.ShortGuid, CancellationToken, Task> RunAsync { get; init; }

    /// <summary>The message that tells the client the stream ended on its own, and it may open it again.</summary>
    public required Func<Documents.Wire.ShortGuid, WorkspaceMessage> Ended { get; init; }

    /// <summary>
    /// The message that tells the client the stream was refused for good, or null when this kind of
    /// stream has no such message and a refusal reads as any other ending.
    /// </summary>
    public Func<Documents.Wire.ShortGuid, RpcException, WorkspaceMessage>? Refused { get; init; }
}
