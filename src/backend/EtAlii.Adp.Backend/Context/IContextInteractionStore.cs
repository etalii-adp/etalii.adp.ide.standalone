using System.Threading.Channels;

namespace EtAlii.Adp.Backend.Context;

/// <summary>
/// Owns, strictly per <c>watch_id</c>, the channel a connection's prompts are pushed down
/// and the interactions currently in flight on it — never shared, even between two
/// connections to the same project, mirroring <c>IHierarchyModelStore</c>'s discipline.
/// </summary>
public interface IContextInteractionStore
{
    /// <summary>Claims this connection's prompt channel; called as its stream opens.</summary>
    void Register(ShortGuid watchId, ChannelWriter<HierarchyMessage> writer);

    /// <summary>Discards a connection's prompt channel and every interaction started on it.</summary>
    void Remove(ShortGuid watchId);

    /// <summary>
    /// Pushes a prompt down one connection's own stream. Returns false when that connection
    /// has no open stream (it never registered, or already went away).
    /// </summary>
    bool TryPush(ShortGuid watchId, ContextPrompt prompt);

    /// <summary>Records an interaction as in flight on its connection.</summary>
    void Begin(ContextInteraction interaction);

    /// <summary>The interaction with this id, or null once it has been completed or its connection went away.</summary>
    ContextInteraction? Get(ShortGuid interactionId);

    /// <summary>
    /// Ends an interaction, so a repeated submit or cancel for the same id is a clean miss
    /// rather than a second execution.
    /// </summary>
    void Complete(ShortGuid interactionId);
}
