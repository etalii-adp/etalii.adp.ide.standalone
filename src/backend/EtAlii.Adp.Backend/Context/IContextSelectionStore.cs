using System.Threading.Channels;

namespace EtAlii.Adp.Backend.Context;

/// <summary>
/// Re-derives a record's actions after the things it names changed underneath it.
/// Supplied by the service, so the store itself never learns about action providers.
/// </summary>
public delegate ValueTask<ContextSelectionRecord> ContextRediscovery(ContextSelectionRecord record, CancellationToken cancellationToken);

/// <summary>
/// Owns, strictly per <c>watch_id</c>, a connection's current selection, the stream it
/// is pushed down, and the observations that keep it current - never shared, even
/// between two connections to the same project, mirroring
/// <c>IHierarchyModelStore</c> and <see cref="IContextInteractionStore"/>.
/// </summary>
public interface IContextSelectionStore
{
    /// <summary>
    /// Claims this connection's stream and writes the current state as its first message.
    /// A second registration for the same id supersedes the first.
    /// </summary>
    /// <param name="rootPath">The project root's path.</param>
    /// <param name="writer">The connection's stream.</param>
    /// <param name="rootActions">
    /// The project root's actions, carried on every "nothing selected" message this
    /// connection receives - the baseline and each later clear - so the explorer's empty
    /// space has its menu without asking.
    /// </param>
    /// <param name="watchId">The connection's id.</param>
    /// <param name="projectActions">The project's actions.</param>
    void Register(
        ShortGuid watchId,
        string rootPath,
        ChannelWriter<ContextMessage> writer,
        IReadOnlyList<ContextActionGroupDefinition> rootActions,
        IReadOnlyList<ContextActionGroupDefinition> projectActions);

    /// <summary>
    /// Writes one project-actions message to every connection in <paramref name="rootPath"/>'s
    /// project, and to none in another - so undo/redo availability follows the history without
    /// disturbing anyone's selection (diagram-undo-redo Requirement 5.3, Deviation 1).
    /// </summary>
    void PushProjectActions(string rootPath, IReadOnlyList<ContextActionGroupDefinition> actions);

    /// <summary>Drops a connection's stream, selection and observations.</summary>
    void Remove(ShortGuid watchId);

    /// <summary>The connection's current selection, for providers and actions without an explicit target.</summary>
    ContextSelectionRecord? Get(ShortGuid watchId);

    /// <summary>Replaces the current selection, starts observing its levels, and pushes it.</summary>
    void Set(ShortGuid watchId, string rootPath, ContextSelectionRecord record, ContextRediscovery rediscover);

    /// <summary>Records "nothing selected" and pushes it.</summary>
    void Clear(ShortGuid watchId);

    /// <summary>
    /// Re-derives the current selection's actions and pushes the result. A completed action can
    /// change what applies to the very same selection - a collapse must offer Expand next - and
    /// the selection itself never moved, so no observation triggers the rediscovery for us.
    /// Nothing selected, or an unknown connection: nothing happens.
    /// </summary>
    void Refresh(ShortGuid watchId);

    /// <summary>Pushes a selection without making it current (a preview).</summary>
    void PushTransient(ShortGuid watchId, ContextSelectionRecord record);

    /// <summary>
    /// A tracked level moved (new relative path) or vanished (null): rewrite or clear the
    /// current selection and push the result.
    /// </summary>
    void UpdateFromTrack(ShortGuid watchId, int levelIndex, IReadOnlyList<string>? newRelativePath);
}
