using EtAlii.Adp.Documents;
using EtAlii.Adp.History;
using EtAlii.Adp.Specification.Fbl.Planning;

namespace EtAlii.Adp.Diagram.Databricks;

/// <summary>Adds a task after the job's last one (Requirement 11.2).</summary>
/// <param name="BodyPath">The resource file.</param>
/// <param name="JobKey">Which job; empty means the file's first.</param>
/// <param name="TaskKey">The new task's key.</param>
/// <param name="TaskType">Its type - notebook, python, condition and kin.</param>
/// <param name="Source">The type's principal source, e.g. a notebook path.</param>
public sealed record InsertDatabricksTaskCommand(
    string BodyPath, string JobKey, string TaskKey, string TaskType, string Source) : IDocumentEditCommand;

/// <summary>
/// Removes a task and every <c>depends_on</c> reference to it, as one command with one inverse
/// (Requirement 11.5) - the edges go with the task, and one undo brings back both.
/// </summary>
public sealed record RemoveDatabricksTaskCommand(string BodyPath, string JobKey, string TaskKey) : IDocumentEditCommand;

/// <summary>Adds a dependency: <paramref name="ToKey"/> comes to wait for <paramref name="FromKey"/> (Requirement 11.3).</summary>
public sealed record ConnectDatabricksTasksCommand(
    string BodyPath, string JobKey, string FromKey, string ToKey, string Outcome = "") : IDocumentEditCommand;

/// <summary>Removes one dependency edge.</summary>
public sealed record DisconnectDatabricksTasksCommand(
    string BodyPath, string JobKey, string FromKey, string ToKey) : IDocumentEditCommand;

/// <summary>
/// Renames a task, rewriting its own key and every reference to it in the same operation - so
/// no reference is ever left stranded (Requirement 11.4).
/// </summary>
public sealed record RenameDatabricksTaskCommand(
    string BodyPath, string JobKey, string TaskKey, string NewKey) : IDocumentEditCommand;

/// <summary>Rewrites a task's <c>run_if</c>; empty restores the schema default.</summary>
public sealed record SetDatabricksRunIfCommand(
    string BodyPath, string JobKey, string TaskKey, string RunIf) : IDocumentEditCommand;

/// <summary>Rebinds a task's cluster; empty means serverless.</summary>
public sealed record SetDatabricksClusterCommand(
    string BodyPath, string JobKey, string TaskKey, string ClusterKey) : IDocumentEditCommand;

/// <inheritdoc cref="InsertDatabricksTaskCommand" />
public sealed class InsertDatabricksTaskCommandHandler(IDatabricksDocumentStore documents)
    : DatabricksEditHandler<InsertDatabricksTaskCommand>(documents)
{
    /// <inheritdoc />
    /// <remarks>The binding has a rule per task type that adds one, and each carries its type's skeleton (FBL 0.10 section 6.2, gap 6).</remarks>
    protected override EditOutcome Apply(LineDocument body, DatabricksModel model, InsertDatabricksTaskCommand command)
    {
        if (JobOf(model, command.JobKey) is not { } job)
        {
            return EditOutcome.Refused("There is no job in this file to add a task to.");
        }

        if (command.TaskKey.Length == 0)
        {
            return EditOutcome.Refused("A task needs a key.");
        }

        if (job.Tasks.Any(task => task.Key == command.TaskKey))
        {
            return EditOutcome.Refused($"A task named '{command.TaskKey}' is already there.");
        }

        return DatabricksEdits.Job(
            body,
            job,
            _ => new ModelChange.Add("Task", null, DatabricksEdits.Attributes(("key", command.TaskKey), ("taskType", command.TaskType), ("taskSource", command.Source))),
            command.TaskType);
    }
}

/// <inheritdoc cref="RemoveDatabricksTaskCommand" />
public sealed class RemoveDatabricksTaskCommandHandler(IDatabricksDocumentStore documents)
    : DatabricksEditHandler<RemoveDatabricksTaskCommand>(documents)
{
    /// <inheritdoc />
    /// <remarks>The binding's remove cascades to every dependency on the task, and a <c>depends_on</c> left empty goes too, in the one edit.</remarks>
    protected override EditOutcome Apply(LineDocument body, DatabricksModel model, RemoveDatabricksTaskCommand command)
    {
        if (JobOf(model, command.JobKey) is not { } job)
        {
            return EditOutcome.Refused("There is no job in this file any more.");
        }

        if (job.Tasks.All(task => task.Key != command.TaskKey))
        {
            return EditOutcome.Refused($"There is no task named '{command.TaskKey}' any more.");
        }

        return DatabricksEdits.Job(body, job, reading => new ModelChange.Remove(DatabricksEdits.TaskId(reading, command.TaskKey)));
    }
}

/// <inheritdoc cref="ConnectDatabricksTasksCommand" />
public sealed class ConnectDatabricksTasksCommandHandler(IDatabricksDocumentStore documents)
    : DatabricksEditHandler<ConnectDatabricksTasksCommand>(documents)
{
    /// <inheritdoc />
    protected override EditOutcome Apply(LineDocument body, DatabricksModel model, ConnectDatabricksTasksCommand command)
    {
        if (JobOf(model, command.JobKey) is not { } job)
        {
            return EditOutcome.Refused("There is no job in this file any more.");
        }

        if (command.FromKey == command.ToKey)
        {
            return EditOutcome.Refused("A task cannot depend on itself.");
        }

        var to = job.Tasks.FirstOrDefault(task => task.Key == command.ToKey);
        if (to is null || job.Tasks.All(task => task.Key != command.FromKey))
        {
            return EditOutcome.Refused("One of those tasks is not there any more.");
        }

        if (to.DependsOn.Any(dependency => dependency.TaskKey == command.FromKey))
        {
            return EditOutcome.Refused($"'{command.ToKey}' already depends on '{command.FromKey}'.");
        }

        var attributes = command.Outcome.Length > 0
            ? DatabricksEdits.Attributes(("upstreamKey", command.FromKey), ("outcome", command.Outcome))
            : DatabricksEdits.Attributes(("upstreamKey", command.FromKey));
        return DatabricksEdits.Job(body, job, reading => new ModelChange.Add("Dependency", null, attributes, DatabricksEdits.TaskId(reading, command.ToKey)));
    }
}

/// <inheritdoc cref="DisconnectDatabricksTasksCommand" />
public sealed class DisconnectDatabricksTasksCommandHandler(IDatabricksDocumentStore documents)
    : DatabricksEditHandler<DisconnectDatabricksTasksCommand>(documents)
{
    /// <inheritdoc />
    /// <remarks>The <c>depends_on:</c> key goes with its last entry (<c>remove-when-empty</c>), so undoing the connect that made both restores the file.</remarks>
    protected override EditOutcome Apply(LineDocument body, DatabricksModel model, DisconnectDatabricksTasksCommand command)
    {
        if (JobOf(model, command.JobKey) is not { } job)
        {
            return EditOutcome.Refused("There is no job in this file any more.");
        }

        var to = job.Tasks.FirstOrDefault(task => task.Key == command.ToKey);
        if (to is null || to.DependsOn.All(dependency => dependency.TaskKey != command.FromKey))
        {
            return EditOutcome.Refused($"There is no dependency from '{command.FromKey}' to '{command.ToKey}' any more.");
        }

        return DatabricksEdits.Job(body, job, reading => new ModelChange.Remove(DatabricksEdits.DependencyId(reading, command.ToKey, command.FromKey)));
    }
}

/// <inheritdoc cref="RenameDatabricksTaskCommand" />
public sealed class RenameDatabricksTaskCommandHandler(IDatabricksDocumentStore documents)
    : DatabricksEditHandler<RenameDatabricksTaskCommand>(documents)
{
    /// <inheritdoc />
    /// <remarks>A rename rewrites every reference to the old key in the same edit (FBL section 5.7): each dependency's <c>task_key</c> is a reference to a task by its key.</remarks>
    protected override EditOutcome Apply(LineDocument body, DatabricksModel model, RenameDatabricksTaskCommand command)
    {
        if (JobOf(model, command.JobKey) is not { } job)
        {
            return EditOutcome.Refused("There is no job in this file any more.");
        }

        if (command.NewKey.Length == 0)
        {
            return EditOutcome.Refused("A task needs a key.");
        }

        if (job.Tasks.All(task => task.Key != command.TaskKey))
        {
            return EditOutcome.Refused($"There is no task named '{command.TaskKey}' any more.");
        }

        if (command.NewKey != command.TaskKey && job.Tasks.Any(task => task.Key == command.NewKey))
        {
            return EditOutcome.Refused($"A task named '{command.NewKey}' is already there.");
        }

        return DatabricksEdits.Job(body, job, reading => new ModelChange.Set(DatabricksEdits.TaskId(reading, command.TaskKey), DatabricksEdits.Attributes(("key", command.NewKey))));
    }
}

/// <inheritdoc cref="SetDatabricksRunIfCommand" />
public sealed class SetDatabricksRunIfCommandHandler(IDatabricksDocumentStore documents)
    : DatabricksEditHandler<SetDatabricksRunIfCommand>(documents)
{
    /// <inheritdoc />
    /// <remarks>An empty value removes the key, restoring the default (<c>empty: "remove"</c>).</remarks>
    protected override EditOutcome Apply(LineDocument body, DatabricksModel model, SetDatabricksRunIfCommand command) =>
        SetTask(body, model, command.JobKey, command.TaskKey, "runIf", command.RunIf);
}

/// <inheritdoc cref="SetDatabricksClusterCommand" />
public sealed class SetDatabricksClusterCommandHandler(IDatabricksDocumentStore documents)
    : DatabricksEditHandler<SetDatabricksClusterCommand>(documents)
{
    /// <inheritdoc />
    /// <remarks>An empty value removes the key: the task runs serverless.</remarks>
    protected override EditOutcome Apply(LineDocument body, DatabricksModel model, SetDatabricksClusterCommand command)
    {
        if (JobOf(model, command.JobKey) is { } job && command.ClusterKey.Length > 0 && job.Clusters.All(cluster => cluster.Key != command.ClusterKey))
        {
            return EditOutcome.Refused($"There is no cluster named '{command.ClusterKey}' in this job.");
        }

        return SetTask(body, model, command.JobKey, command.TaskKey, "clusterKey", command.ClusterKey);
    }
}
