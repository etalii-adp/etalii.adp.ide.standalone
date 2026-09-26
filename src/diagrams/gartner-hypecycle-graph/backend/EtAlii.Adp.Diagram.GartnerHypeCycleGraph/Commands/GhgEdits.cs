using EtAlii.Adp.Documents;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph;

/// <summary>The one shape every FDG command has: edit a copy, save it, and hand back its undo.</summary>
/// <remarks>
/// <para>
/// <b>Every undo is the shared restore command</b> (backend-centralization R6): the document's whole
/// text as it was before the edit, with the command itself as the redo. So an undo gives back the
/// original bytes exactly, however much the edit moved.
/// </para>
/// <para>
/// <b>The edit is made on a COPY of the cached document</b>, not on the cached one in place. A
/// refusal then leaves nothing to undo in memory as well as on disk: a span edit writes `start`
/// before it can learn that `stop` is refused, and an in-place edit would leave half of it in the
/// cache for the next save to write.
/// </para>
/// <para>
/// <b>A document that could not be read is not edited at all</b>: its emptiness is not the document.
/// </para>
/// </remarks>
internal static class GhgEdits
{
    public static Task<CommandResult> Run(
        IGhgDocumentStore documents,
        string bodyPath,
        ICommand self,
        Func<LineDocument, GhgModel, GhgEdit> edit)
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
        var outcome = edit(document, GhgParser.Parse(document));
        if (!outcome.WasApplied)
        {
            return Task.FromResult(CommandResult.Failure(outcome.Refusal!));
        }

        var saved = documents.Save(bodyPath, document);
        if (saved.Failed)
        {
            return Task.FromResult(CommandResult.Failure(saved.Error));
        }

        var undo = new RestoreDocumentCommand<IGhgDocumentStore>(bodyPath, before, self);
        return Task.FromResult(saved.Warning.Length > 0
            ? CommandResult.Success(undo, saved.Warning)
            : CommandResult.Success(undo));
    }

    /// <summary>The trend with <paramref name="id"/>: the first entry with it, as the canvas draws it.</summary>
    public static GhgTrend? TrendOf(GhgModel model, string id) =>
        model.Trends.FirstOrDefault(trend => string.Equals(trend.Id, id, StringComparison.Ordinal));

    /// <summary>The influence with <paramref name="id"/>: the first entry with it.</summary>
    public static GhgInfluence? InfluenceOf(GhgModel model, string id) =>
        model.Influences.FirstOrDefault(influence => string.Equals(influence.Id, id, StringComparison.Ordinal));

    /// <summary>The refusal for a trend or influence that is not there, in one wording.</summary>
    public static GhgEdit Gone() => GhgEdit.Refused("That is no longer in this graph.");

    /// <summary>The refusal for a trend without a readable span, which nothing can move or resize.</summary>
    public static GhgEdit NoSpan() => GhgEdit.Refused("This trend's dates cannot be read, so it cannot be changed until they are fixed in the file.");

    /// <summary>
    /// The refusal for a span too short for the phases it shows: every phase is at least a month
    /// (Requirement 4.6), so N phases need N months. Null when the span is long enough.
    /// </summary>
    public static GhgEdit? TooShort(int months, int phases) =>
        months < Math.Max(1, phases)
            ? GhgEdit.Refused(phases <= 1
                ? "A trend must be at least one month long."
                : $"A trend showing {phases} phases must be at least {phases} months long, one per phase.")
            : null;
}
