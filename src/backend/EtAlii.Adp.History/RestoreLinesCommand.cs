using EtAlii.Adp.Documents;

namespace EtAlii.Adp.History;

/// <summary>
/// Puts removed lines back exactly where they were - the inverse a removal in a line-based document
/// reports, so undoing one restores the file byte for byte, comments and formatting included.
/// Written once for every module whose body is a <see cref="LineDocument"/> (code-structure FR-033).
/// </summary>
/// <typeparam name="TStore">
/// The module's store, which reloads the restored file. As with <see cref="RestoreDocumentCommand{TStore}"/>,
/// it is what makes each module's restore a type of its own, so the dispatcher reaches the right handler.
/// </typeparam>
/// <param name="BodyPath">The document to restore into.</param>
/// <param name="Segments">The captured runs, each with the index it sat at, ascending.</param>
/// <param name="Redo">The command that removes the lines again: the removal itself, by id.</param>
/// <remarks>
/// <b>Not bound to the document's text, on purpose.</b> It puts lines back at their indexes rather
/// than a whole text back, so it still means something after another program wrote the file, and
/// refuses only when the file has become too short to hold them.
/// </remarks>
public sealed record RestoreLinesCommand<TStore>(string BodyPath, IReadOnlyList<LineSegment> Segments, ICommand Redo) : ICommand
    where TStore : IReloadableDocumentStore;
