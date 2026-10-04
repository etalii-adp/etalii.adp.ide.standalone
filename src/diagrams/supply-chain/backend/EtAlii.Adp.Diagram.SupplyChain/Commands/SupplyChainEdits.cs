using EtAlii.Adp.Documents;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.SupplyChain;

/// <summary>The one shape every supply chain command has: edit a copy, save it, and hand back its undo.</summary>
/// <remarks>
/// <para>
/// <b>Every undo is the shared restore command</b>: the document's whole text as it was, with the
/// command itself as the redo, so an undo gives back the original bytes however much moved.
/// </para>
/// <para>
/// <b>The edit is made on a copy</b> of the cached document, so a refusal halfway through leaves
/// nothing behind in memory for the next save to write. A document that could not be read is not
/// edited at all.
/// </para>
/// </remarks>
internal static class SupplyChainEdits
{
    public static Task<CommandResult> Run(
        ISupplyChainDocumentStore documents,
        string bodyPath,
        ICommand self,
        Func<LineDocument, SupplyChainModel, SupplyChainEdit> edit)
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
        var outcome = edit(document, SupplyChainParser.Parse(document));
        if (!outcome.WasApplied)
        {
            return Task.FromResult(CommandResult.Failure(outcome.Refusal!));
        }

        var saved = documents.Save(bodyPath, document);
        if (saved.Failed)
        {
            return Task.FromResult(CommandResult.Failure(saved.Error));
        }

        var undo = new RestoreDocumentCommand<ISupplyChainDocumentStore>(bodyPath, before, self);
        return Task.FromResult(saved.Warning.Length > 0
            ? CommandResult.Success(undo, saved.Warning)
            : CommandResult.Success(undo));
    }

    /// <summary>The node with <paramref name="id"/>: the first entry with it, as the canvas draws it.</summary>
    public static SupplyChainNode? NodeOf(SupplyChainModel model, string id) =>
        model.Nodes.FirstOrDefault(node => string.Equals(node.Id, id, StringComparison.Ordinal));

    /// <summary>The group with <paramref name="id"/>.</summary>
    public static SupplyChainGroup? GroupOf(SupplyChainModel model, string id) =>
        model.Groups.FirstOrDefault(group => string.Equals(group.Id, id, StringComparison.Ordinal));

    /// <summary>The flow with <paramref name="id"/>.</summary>
    public static SupplyChainFlow? FlowOf(SupplyChainModel model, string id) =>
        model.Flows.FirstOrDefault(flow => string.Equals(flow.Id, id, StringComparison.Ordinal));

    /// <summary>Whether any entry already uses <paramref name="id"/>.</summary>
    public static bool IsTaken(SupplyChainModel model, string id) =>
        model.Groups.Any(group => group.Id == id) || model.Nodes.Any(node => node.Id == id) || model.Flows.Any(flow => flow.Id == id);

    /// <summary>The refusal for an entry that is not there, in one wording.</summary>
    public static SupplyChainEdit Gone() => SupplyChainEdit.Refused("That is no longer in this diagram.");

    /// <summary>The name, or the name with the lowest number that makes it unique among <paramref name="taken"/>.</summary>
    public static string Unique(IEnumerable<string> taken, string name)
    {
        var names = taken.ToHashSet(StringComparer.Ordinal);
        if (!names.Contains(name))
        {
            return name;
        }

        for (var number = 2; ; number++)
        {
            var candidate = $"{name} {number}";
            if (!names.Contains(candidate))
            {
                return candidate;
            }
        }
    }
}
