using System.Collections.Concurrent;
using System.Threading.Channels;
using Serilog;

namespace EtAlii.Adp.Backend.Context;

/// <inheritdoc cref="IContextInteractionStore" />
public sealed class ContextInteractionStore : IContextInteractionStore
{
    private static readonly ILogger _logger = Log.ForContext<ContextInteractionStore>();

    private readonly ConcurrentDictionary<ShortGuid, ContextInteractionConnection> _connections = new();

    // A second index so an interaction id alone identifies its owning connection; the
    // connection entry remains the authority on what that connection currently holds.
    private readonly ConcurrentDictionary<ShortGuid, ContextInteraction> _interactions = new();

    public void Register(ShortGuid watchId, ChannelWriter<ContextMessage> writer) =>
        _connections[watchId] = new ContextInteractionConnection(writer);

    public void Remove(ShortGuid watchId)
    {
        if (!_connections.TryRemove(watchId, out var connection))
        {
            return;
        }

        if (!connection.InteractionIds.IsEmpty)
        {
            // Dialogs the user had open when the stream went away. Nothing was committed, so
            // this is not a failure - but a stream of these means clients are dropping their
            // connection mid-dialog.
            _logger.Debug(
                "Dropping {Count} unfinished interactions with watch {WatchId}",
                connection.InteractionIds.Count,
                watchId);
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
            _logger.Warning(
                "Interaction {InteractionId} for {ActionId} was dropped: watch {WatchId} is not registered",
                interaction.Id,
                interaction.ActionId,
                interaction.WatchId);
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

}
