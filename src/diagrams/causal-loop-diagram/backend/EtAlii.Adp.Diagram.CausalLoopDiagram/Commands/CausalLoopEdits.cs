using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.CausalLoopDiagram;

/// <summary>
/// The discipline every edit command in this module shares: check the document reads, capture its
/// bytes, run one writer operation - which refuses before any splice - save through the store, and
/// report a byte-exact restore as the inverse.
/// </summary>
/// <remarks>
/// The inverse is a whole-document snapshot rather than a reversing edit. Some operations here
/// touch several distant lines at once - renaming a variable moves every link and loop that names
/// it - the documents are small, and a snapshot cannot be wrong about what it is putting back. The
/// redo of that inverse is the original command, so redoing re-runs the same edit.
/// </remarks>
internal static class CausalLoopEdits
{
    /// <summary>Runs one writer edit as a command body; see the class remarks.</summary>
    public static Task<CommandResult> Run(
        ICausalLoopDocumentStore documents,
        string bodyPath,
        ICommand self,
        Func<CausalLoopDocumentEntry, string> edit)
    {
        var entry = documents.GetOrLoad(bodyPath);
        if (!entry.IsUsable)
        {
            return Task.FromResult(CommandResult.Failure(
                "This causal loop diagram could not be read, so nothing can be edited until it is fixed."));
        }

        var before = entry.Document.Text;

        var refusal = edit(entry);
        if (refusal.Length > 0)
        {
            return Task.FromResult(CommandResult.Failure(refusal));
        }

        var saved = documents.Save(bodyPath, entry);
        return Task.FromResult(saved.Failed
            ? CommandResult.Failure(saved.Error)
            : CommandResult.Success(new RestoreDocumentCommand<ICausalLoopDocumentStore>(bodyPath, before, self)));
    }
}
