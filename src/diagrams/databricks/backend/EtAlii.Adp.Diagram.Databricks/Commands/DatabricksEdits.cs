using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.Databricks;

/// <summary>
/// The discipline every module edit command shares: check the document parses, capture its
/// bytes, run one writer operation (which refuses before any splice), save through the store -
/// and report a byte-exact restore as the inverse.
/// </summary>
/// <remarks>
/// The inverse is a whole-document snapshot rather than the timeline's per-segment capture,
/// deliberately: this family's operations touch several distant ranges at once
/// (rename-with-references, remove-with-edges), the documents are small, and a snapshot cannot
/// be wrong. The redo of that inverse is the original command instance, so a redo re-runs the
/// same edit against the restored document.
/// </remarks>
internal static class DatabricksEdits
{
    /// <summary>Runs one writer edit as a command body; see the class remarks.</summary>
    public static Task<CommandResult> Run(
        IDatabricksDocumentStore documents,
        string bodyPath,
        ICommand self,
        Func<DatabricksDocumentEntry, string> edit)
    {
        var entry = documents.GetOrLoad(bodyPath);
        if (!entry.IsUsable)
        {
            return Task.FromResult(CommandResult.Failure(
                "This file does not parse, so nothing can be edited until it is fixed."));
        }

        var before = entry.Document.Text;
        var refusal = edit(entry);
        if (refusal.Length > 0)
        {
            return Task.FromResult(CommandResult.Failure(refusal));
        }

        var error = documents.Save(bodyPath, entry);
        return Task.FromResult(error.Length == 0
            ? CommandResult.Success(new RestoreDocumentCommand<IDatabricksDocumentStore>(bodyPath, before, self))
            : CommandResult.Failure(error));
    }

    /// <summary>The job an edit addresses: the named one, or the file's first.</summary>
    public static JobModel? JobOf(DatabricksDocumentEntry entry, string jobKey) =>
        jobKey.Length > 0
            ? entry.Jobs.FirstOrDefault(job => job.Key == jobKey)
            : entry.Jobs.FirstOrDefault();

    /// <summary>The pipeline an edit addresses: the named one, or the file's first.</summary>
    public static PipelineModel? PipelineOf(DatabricksDocumentEntry entry, string pipelineKey) =>
        pipelineKey.Length > 0
            ? entry.Pipelines.FirstOrDefault(pipeline => pipeline.Key == pipelineKey)
            : entry.Pipelines.FirstOrDefault();
}
