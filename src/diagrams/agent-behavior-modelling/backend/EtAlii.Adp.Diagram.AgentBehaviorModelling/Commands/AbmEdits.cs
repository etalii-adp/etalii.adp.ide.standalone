using EtAlii.Adp.History;
using EtAlii.Adp.Specification.Fbl.Planning;

namespace EtAlii.Adp.Diagram.AgentBehaviorModelling;

/// <summary>The one shape every behavior model command has: change a copy, save it, and hand back its undo.</summary>
/// <remarks>
/// <para>
/// <b>Every undo is the shared restore command</b> (backend-centralization R6): the Markdown's whole
/// text as it was before the edit, with the command itself as the redo. So an undo gives back the
/// author's bytes exactly, however far a move re-indented a subtree.
/// </para>
/// <para>
/// <b>The edit is made on a copy</b>, so a refusal leaves nothing half-done in the cache, and a
/// document that could not be read is not edited at all: its emptiness is not the document.
/// </para>
/// <para>
/// <b>Every edit is a <see cref="ModelChange"/></b> made on an
/// <see cref="AbmBody"/>: the persistence plugin plans it as the lines it touches, as
/// <see cref="AbmWriter"/> writes them.
/// </para>
/// </remarks>
internal static class AbmEdits
{
    public static Task<CommandResult> Run(
        IAbmDocumentStore documents,
        string bodyPath,
        ICommand self,
        Func<AbmBody, AbmModel, AbmEdit> edit)
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
        var document = AbmBody.Parse(before);
        var outcome = edit(document, document.Model);
        if (!outcome.WasApplied)
        {
            return Task.FromResult(CommandResult.Failure(outcome.Refusal!));
        }

        var saved = documents.Save(bodyPath, document);
        if (saved.Failed)
        {
            return Task.FromResult(CommandResult.Failure(saved.Error));
        }

        var undo = new RestoreDocumentCommand<IAbmDocumentStore>(bodyPath, before, self);
        return Task.FromResult(saved.Warning.Length > 0
            ? CommandResult.Success(undo, saved.Warning)
            : CommandResult.Success(undo));
    }

    /// <summary>The node type of <paramref name="kind"/>; a kind that is none of the eleven as itself, for the plugin to refuse in its words.</summary>
    public static string TypeOf(string kind) => AbmDefinition.TypeOfKind.GetValueOrDefault(kind, kind);

    /// <summary>The change that sets <paramref name="node"/>'s <paramref name="attribute"/> to <paramref name="value"/>.</summary>
    public static ModelChange Set(AbmNode node, string attribute, object? value) =>
        new ModelChange.Set(node.Id, new Dictionary<string, object?>(StringComparer.Ordinal) { [attribute] = value });

    /// <summary>The refusal for a node that is not there, in one wording.</summary>
    public static AbmEdit Gone() => AbmEdit.Refused("That node is no longer in this behavior model.");
}
