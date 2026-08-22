using System.Collections.Concurrent;
using System.Threading.Channels;

namespace EtAlii.Adp.Backend.Context;

/// <inheritdoc cref="IContextInteractionStore" />
public sealed class ContextInteractionStore : IContextInteractionStore
{
    private readonly ConcurrentDictionary<ShortGuid, Connection> _connections = new();

    // A second index so an interaction id alone identifies its owning connection; the
    // connection entry remains the authority on what that connection currently holds.
    private readonly ConcurrentDictionary<ShortGuid, ContextInteraction> _interactions = new();

    public void Register(ShortGuid watchId, ChannelWriter<ContextMessage> writer) =>
        _connections[watchId] = new Connection(writer);

    public void Remove(ShortGuid watchId)
    {
        if (!_connections.TryRemove(watchId, out var connection))
        {
            return;
        }

        foreach (var interactionId in connection.InteractionIds.Keys)
        {
            _interactions.TryRemove(interactionId, out _);
        }
    }

    public bool TryPush(ShortGuid watchId, ContextPrompt prompt)
    {
        // Only ever this exact connection's writer: a prompt raised on one connection must
        // never become observable on another, even for the same project and user.
        if (!_connections.TryGetValue(watchId, out var connection))
        {
            return false;
        }

        return connection.Writer.TryWrite(new ContextMessage { Prompt = prompt });
    }

    public void Begin(ContextInteraction interaction)
    {
        if (!_connections.TryGetValue(interaction.WatchId, out var connection))
        {
            return;
        }

        connection.InteractionIds[interaction.Id] = 0;
        _interactions[interaction.Id] = interaction;
    }

    public ContextInteraction? Get(ShortGuid interactionId) =>
        _interactions.TryGetValue(interactionId, out var interaction) ? interaction : null;

    public void Complete(ShortGuid interactionId)
    {
        if (!_interactions.TryRemove(interactionId, out var interaction))
        {
            return;
        }

        if (_connections.TryGetValue(interaction.WatchId, out var connection))
        {
            connection.InteractionIds.TryRemove(interactionId, out _);
        }
    }

    private sealed class Connection
    {
        public Connection(ChannelWriter<ContextMessage> writer)
        {
            Writer = writer;
        }

        public ChannelWriter<ContextMessage> Writer { get; }

        /// <summary>Used as a concurrent set; the byte value carries no meaning.</summary>
        public ConcurrentDictionary<ShortGuid, byte> InteractionIds { get; } = new();
    }
}
