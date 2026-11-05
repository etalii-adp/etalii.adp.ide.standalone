using EtAlii.Adp.Documents;
using EtAlii.Adp.History;
using EtAlii.Adp.Specification.Fbl.Planning;

namespace EtAlii.Adp.Diagram.Databricks;

/// <summary>
/// A handler of a command that edits a Databricks file: the shared run, which edits a copy, saves
/// it and carries a warning of the save to the result (code-structure FR-033), with the model the
/// binding reads; every edit is a model change written by <see cref="DatabricksEdits"/>.
/// </summary>
/// <remarks>
/// The inverse is the run's whole-document restore rather than a per-segment capture,
/// deliberately: this family's operations touch several distant ranges at once
/// (rename-with-references, remove-with-edges), the documents are small, and a snapshot cannot
/// be wrong. The redo of that inverse is the original command, so a redo re-runs the same edit
/// against the restored document.
/// </remarks>
/// <typeparam name="TCommand">The command handled.</typeparam>
public abstract class DatabricksEditHandler<TCommand>(IDatabricksDocumentStore documents)
    : DocumentEditHandler<TCommand, IDatabricksDocumentStore, LineDocument, DatabricksModel>(documents)
    where TCommand : IDocumentEditCommand
{
    /// <inheritdoc />
    protected sealed override DatabricksModel Parse(LineDocument body) => DatabricksModel.Read(body);

    /// <summary>Sets one attribute of the task keyed <paramref name="taskKey"/> of the job an edit addresses.</summary>
    protected static EditOutcome SetTask(LineDocument body, DatabricksModel model, string jobKey, string taskKey, string attribute, string value)
    {
        if (JobOf(model, jobKey) is not { } job)
        {
            return EditOutcome.Refused("There is no job in this file any more.");
        }

        return job.Tasks.All(task => task.Key != taskKey)
            ? EditOutcome.Refused($"There is no task named '{taskKey}' any more.")
            : DatabricksEdits.Job(body, job, reading => new ModelChange.Set(DatabricksEdits.TaskId(reading, taskKey), DatabricksEdits.Attributes((attribute, value))));
    }

    /// <summary>The job an edit addresses: the named one, or the file's first.</summary>
    protected static JobModel? JobOf(DatabricksModel model, string jobKey)
    {
        ArgumentNullException.ThrowIfNull(model);
        return jobKey.Length > 0
            ? model.Jobs.FirstOrDefault(job => job.Key == jobKey)
            : model.Jobs.FirstOrDefault();
    }

    /// <summary>The pipeline an edit addresses: the named one, or the file's first.</summary>
    protected static PipelineModel? PipelineOf(DatabricksModel model, string pipelineKey)
    {
        ArgumentNullException.ThrowIfNull(model);
        return pipelineKey.Length > 0
            ? model.Pipelines.FirstOrDefault(pipeline => pipeline.Key == pipelineKey)
            : model.Pipelines.FirstOrDefault();
    }
}
