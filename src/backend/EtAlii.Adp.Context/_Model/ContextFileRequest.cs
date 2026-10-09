namespace EtAlii.Adp.Context;

/// <summary>
/// A dialog asking the user to pick one file of the workspace: its files as a tree, limited to
/// those the asking provider accepts, with entries the provider pins shown first. The answer is
/// the file's project-relative path, its segments joined by <c>/</c>.
/// </summary>
/// <remarks>
/// It names no tool type. Which files qualify is the provider's to say, as a predicate over a
/// file's full path, so the same dialog serves whoever needs a file chosen.
/// <para>
/// The predicate is asked on the backend, when the prompt is built, and what it accepted is
/// what a submission is checked against: a path the dialog did not offer is refused before the
/// provider's commit runs, whatever a client sends.
/// </para>
/// </remarks>
/// <param name="Title">The dialog's title.</param>
/// <param name="Icon">The @mdi/font class of the dialog's icon.</param>
/// <param name="ConfirmLabel">The label of the button that confirms the choice.</param>
/// <param name="Accepts">Whether the file at this full path may be chosen.</param>
/// <param name="EmptyMessage">Shown in the tree's place when no file qualifies and nothing is pinned.</param>
/// <param name="Pinned">Entries shown before the tree, in this order.</param>
public sealed record ContextFileRequest(
    string Title,
    string Icon,
    string ConfirmLabel,
    Func<string, bool> Accepts,
    string EmptyMessage,
    IReadOnlyList<ContextPinnedFile>? Pinned = null)
{
    public IReadOnlyList<ContextPinnedFile> Pinned { get; } = Pinned ?? [];
}

/// <summary>
/// A file a file dialog shows first, under a label of the provider's choosing - <i>this file</i>,
/// say. It is offered whether or not the request's predicate accepts it.
/// </summary>
/// <param name="Label">What the entry reads.</param>
/// <param name="FullPath">The file's full path, inside the project.</param>
public sealed record ContextPinnedFile(string Label, string FullPath);
