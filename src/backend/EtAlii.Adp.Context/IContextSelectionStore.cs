using System.Threading.Channels;
using EtAlii.Adp.Common;
using EtAlii.Adp.Common.Wire;
using EtAlii.Adp.Context.Wire;

namespace EtAlii.Adp.Context;

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
    /// <param name="problems">
    /// The project's current problems, carried as part of the baseline so the panel is
    /// current the moment it connects (errors-and-warnings-panel Requirement 1.2). Data,
    /// not a store: this store never learns where problems come from.
    /// </param>
    void Register(
        ShortGuid watchId,
        string rootPath,
        ChannelWriter<ContextMessage> writer,
        IReadOnlyList<ContextActionGroupDefinition> rootActions,
        IReadOnlyList<ContextActionGroupDefinition> projectActions,
        ProjectProblems problems);

    /// <summary>
    /// Writes one project-actions message to every connection in <paramref name="rootPath"/>'s
    /// project, and to none in another - so undo/redo availability follows the history without
    /// disturbing anyone's selection (diagram-undo-redo Requirement 5.3, Deviation 1).
    /// </summary>
    void PushProjectActions(string rootPath, IReadOnlyList<ContextActionGroupDefinition> actions);

    /// <summary>
    /// Writes one problems message to every connection in <paramref name="rootPath"/>'s
    /// project, and to none in another - the reason two viewers of one project cannot
    /// disagree about what is wrong (errors-and-warnings-panel Requirements 1.1, 6.4).
    /// </summary>
    void PushProblems(string rootPath, ProjectProblems problems);

    /// <summary>
    /// Writes one notice to every connection in <paramref name="rootPath"/>'s project: a
    /// thing that happened alongside a command that SUCCEEDED and that the user would
    /// otherwise discover only later.
    /// </summary>
    /// <remarks>
    /// Broadcast to the project rather than answered to the caller, because the outcome
    /// belongs to the document rather than to the connection that happened to cause it: a
    /// position that was not recorded is missing for whoever opens the diagram next, not
    /// only for whoever dragged it.
    /// </remarks>
    void PushNotice(string rootPath, string message);

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
