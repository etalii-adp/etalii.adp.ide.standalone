using EtAlii.Adp.Documents;
using EtAlii.Adp.History;

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
    protected override EditOutcome Apply(LineDocument body, DatabricksModel model, InsertDatabricksTaskCommand command) => Outcome(
        JobOf(model, command.JobKey) is { } job
            ? JobWriter.InsertTask(body, job, command.TaskKey, command.TaskType, command.Source)
            : "There is no job in this file to add a task to.");
}

/// <inheritdoc cref="RemoveDatabricksTaskCommand" />
public sealed class RemoveDatabricksTaskCommandHandler(IDatabricksDocumentStore documents)
    : DatabricksEditHandler<RemoveDatabricksTaskCommand>(documents)
{
    /// <inheritdoc />
    protected override EditOutcome Apply(LineDocument body, DatabricksModel model, RemoveDatabricksTaskCommand command) => Outcome(
        JobOf(model, command.JobKey) is { } job
            ? JobWriter.RemoveTask(body, job, command.TaskKey)
            : "There is no job in this file any more.");
}

/// <inheritdoc cref="ConnectDatabricksTasksCommand" />
public sealed class ConnectDatabricksTasksCommandHandler(IDatabricksDocumentStore documents)
    : DatabricksEditHandler<ConnectDatabricksTasksCommand>(documents)
{
    /// <inheritdoc />
    protected override EditOutcome Apply(LineDocument body, DatabricksModel model, ConnectDatabricksTasksCommand command) => Outcome(
        JobOf(model, command.JobKey) is { } job
            ? JobWriter.Connect(body, job, command.FromKey, command.ToKey, command.Outcome)
            : "There is no job in this file any more.");
}

/// <inheritdoc cref="DisconnectDatabricksTasksCommand" />
public sealed class DisconnectDatabricksTasksCommandHandler(IDatabricksDocumentStore documents)
    : DatabricksEditHandler<DisconnectDatabricksTasksCommand>(documents)
{
    /// <inheritdoc />
    protected override EditOutcome Apply(LineDocument body, DatabricksModel model, DisconnectDatabricksTasksCommand command) => Outcome(
        JobOf(model, command.JobKey) is { } job
            ? JobWriter.Disconnect(body, job, command.FromKey, command.ToKey)
            : "There is no job in this file any more.");
}

/// <inheritdoc cref="RenameDatabricksTaskCommand" />
public sealed class RenameDatabricksTaskCommandHandler(IDatabricksDocumentStore documents)
    : DatabricksEditHandler<RenameDatabricksTaskCommand>(documents)
{
    /// <inheritdoc />
    protected override EditOutcome Apply(LineDocument body, DatabricksModel model, RenameDatabricksTaskCommand command) => Outcome(
        JobOf(model, command.JobKey) is { } job
            ? JobWriter.RenameTask(body, job, command.TaskKey, command.NewKey)
            : "There is no job in this file any more.");
}

/// <inheritdoc cref="SetDatabricksRunIfCommand" />
public sealed class SetDatabricksRunIfCommandHandler(IDatabricksDocumentStore documents)
    : DatabricksEditHandler<SetDatabricksRunIfCommand>(documents)
{
    /// <inheritdoc />
    protected override EditOutcome Apply(LineDocument body, DatabricksModel model, SetDatabricksRunIfCommand command) => Outcome(
        JobOf(model, command.JobKey) is { } job
            ? JobWriter.SetRunIf(body, job, command.TaskKey, command.RunIf)
            : "There is no job in this file any more.");
}

/// <inheritdoc cref="SetDatabricksClusterCommand" />
public sealed class SetDatabricksClusterCommandHandler(IDatabricksDocumentStore documents)
    : DatabricksEditHandler<SetDatabricksClusterCommand>(documents)
{
    /// <inheritdoc />
    protected override EditOutcome Apply(LineDocument body, DatabricksModel model, SetDatabricksClusterCommand command) => Outcome(
        JobOf(model, command.JobKey) is { } job
            ? JobWriter.SetCluster(body, job, command.TaskKey, command.ClusterKey)
            : "There is no job in this file any more.");
}
