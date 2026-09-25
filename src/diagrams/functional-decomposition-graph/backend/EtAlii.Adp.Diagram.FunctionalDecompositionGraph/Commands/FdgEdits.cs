using EtAlii.Adp.Documents;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.FunctionalDecompositionGraph;

/// <summary>The one shape every FDG command has: edit a copy, save it, and hand back its undo.</summary>
/// <remarks>
/// <para>
/// <b>Every undo is the shared restore command</b> (backend-centralization R6): the document's whole
/// text as it was before the edit, with the command itself as the redo. So an undo gives back the
/// original bytes exactly, however much the edit moved.
/// </para>
/// <para>
/// <b>The edit is made on a COPY of the cached document</b>, not on the cached one in place. A
/// refusal then leaves nothing to undo in memory as well as on disk. That is not hypothetical:
/// <see cref="FdgWriter.Resize"/> writes the width before it refuses a height for anything but a
/// Comment, so an in-place edit would leave half a resize in the cache for the next save to write.
/// </para>
/// <para>
/// <b>A document that could not be read is not edited at all</b>: its emptiness is not the document.
/// </para>
/// </remarks>
internal static class FdgEdits
{
    public static Task<CommandResult> Run(
        IFdgDocumentStore documents,
        string bodyPath,
        ICommand self,
        Func<LineDocument, FdgModel, FdgEdit> edit)
    {
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentException.ThrowIfNullOrWhiteSpace(bodyPath);
        ArgumentNullException.ThrowIfNull(self);
        ArgumentNullException.ThrowIfNull(edit);

        var entry = documents.GetOrLoad(bodyPath);
        if (!entry.IsUsable)
        {
            return Task.FromResult(CommandResult.Failure(
                $"{Path.GetFileName(bodyPath)} could not be read, so nothing can be edited until it can: {entry.Unreadable}"));
        }

        var before = entry.Document.Text;
        var document = LineDocument.Parse(before);
        var outcome = edit(document, FdgParser.Parse(document));
        if (!outcome.WasApplied)
        {
            return Task.FromResult(CommandResult.Failure(outcome.Refusal!));
        }

        var saved = documents.Save(bodyPath, document);
        if (saved.Failed)
        {
            return Task.FromResult(CommandResult.Failure(saved.Error));
        }

        var undo = new RestoreDocumentCommand<IFdgDocumentStore>(bodyPath, before, self);
        return Task.FromResult(saved.Warning.Length > 0
            ? CommandResult.Success(undo, saved.Warning)
            : CommandResult.Success(undo));
    }

    /// <summary>The element with <paramref name="id"/>: the first entry with it, as the canvas draws it.</summary>
    public static FdgElement? ElementOf(FdgModel model, string id) =>
        model.Elements.FirstOrDefault(element => string.Equals(element.Id, id, StringComparison.Ordinal));

    /// <summary>The connection with <paramref name="id"/>: the first entry with it.</summary>
    public static FdgConnection? ConnectionOf(FdgModel model, string id) =>
        model.Connections.FirstOrDefault(connection => string.Equals(connection.Id, id, StringComparison.Ordinal));

    /// <summary>The refusal for an element that is not there, in one wording.</summary>
    public static FdgEdit Gone() => FdgEdit.Refused("That element is no longer in this graph.");
}
