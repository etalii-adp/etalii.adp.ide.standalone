using System.Collections.Concurrent;
using System.Threading.Channels;
using EtAlii.Adp.Context.Wire;

namespace EtAlii.Adp.Context;

internal sealed class ContextInteractionConnection
{
    public ContextInteractionConnection(ChannelWriter<ContextMessage> writer)
    {
        Writer = writer;
    }

    public ChannelWriter<ContextMessage> Writer { get; }

    /// <summary>Used as a concurrent set; the byte value carries no meaning.</summary>
    public ConcurrentDictionary<ShortGuid, byte> InteractionIds { get; } = new();
}
