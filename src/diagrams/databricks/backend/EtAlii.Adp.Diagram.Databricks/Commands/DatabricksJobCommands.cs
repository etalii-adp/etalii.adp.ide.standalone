using EtAlii.Adp.Backend;


using EtAlii.Adp.Common;
namespace EtAlii.Adp.Diagram.Databricks;

/// <summary>Adds a task after the job's last one (Requirement 11.2).</summary>
/// <param name="BodyPath">The resource file.</param>
/// <param name="JobKey">Which job; empty means the file's first.</param>
/// <param name="TaskKey">The new task's key.</param>
/// <param name="TaskType">Its type - notebook, python, condition and kin.</param>
/// <param name="Source">The type's principal source, e.g. a notebook path.</param>
public sealed record InsertDatabricksTaskCommand(
    string BodyPath, string JobKey, string TaskKey, string TaskType, string Source) : ICommand;

/// <summary>
/// Removes a task and every <c>depends_on</c> reference to it, as one command with one inverse
/// (Requirement 11.5) - the edges go with the task, and one undo brings back both.
/// </summary>
public sealed record RemoveDatabricksTaskCommand(string BodyPath, string JobKey, string TaskKey) : ICommand;

/// <summary>Adds a dependency: <paramref name="ToKey"/> comes to wait for <paramref name="FromKey"/> (Requirement 11.3).</summary>
public sealed record ConnectDatabricksTasksCommand(
    string BodyPath, string JobKey, string FromKey, string ToKey, string Outcome = "") : ICommand;

/// <summary>Removes one dependency edge.</summary>
public sealed record DisconnectDatabricksTasksCommand(
    string BodyPath, string JobKey, string FromKey, string ToKey) : ICommand;

/// <summary>
/// Renames a task, rewriting its own key and every reference to it in the same operation - so
/// no reference is ever left stranded (Requirement 11.4).
/// </summary>
public sealed record RenameDatabricksTaskCommand(
    string BodyPath, string JobKey, string TaskKey, string NewKey) : ICommand;

/// <summary>Rewrites a task's <c>run_if</c>; empty restores the schema default.</summary>
public sealed record SetDatabricksRunIfCommand(
    string BodyPath, string JobKey, string TaskKey, string RunIf) : ICommand;

/// <summary>Rebinds a task's cluster; empty means serverless.</summary>
public sealed record SetDatabricksClusterCommand(
    string BodyPath, string JobKey, string TaskKey, string ClusterKey) : ICommand;

/// <inheritdoc cref="InsertDatabricksTaskCommand" />
public sealed class InsertDatabricksTaskCommandHandler(IDatabricksDocumentStore documents)
    : ICommandHandler<InsertDatabricksTaskCommand>
{
    public Task<CommandResult> ExecuteAsync(InsertDatabricksTaskCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        return DatabricksEdits.Run(documents, command.BodyPath, command, entry =>
            DatabricksEdits.JobOf(entry, command.JobKey) is { } job
                ? JobWriter.InsertTask(entry.Document, job, command.TaskKey, command.TaskType, command.Source)
                : "There is no job in this file to add a task to.");
    }
}

/// <inheritdoc cref="RemoveDatabricksTaskCommand" />
public sealed class RemoveDatabricksTaskCommandHandler(IDatabricksDocumentStore documents)
    : ICommandHandler<RemoveDatabricksTaskCommand>
{
    public Task<CommandResult> ExecuteAsync(RemoveDatabricksTaskCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        return DatabricksEdits.Run(documents, command.BodyPath, command, entry =>
            DatabricksEdits.JobOf(entry, command.JobKey) is { } job
                ? JobWriter.RemoveTask(entry.Document, job, command.TaskKey)
                : "There is no job in this file any more.");
    }
}

/// <inheritdoc cref="ConnectDatabricksTasksCommand" />
public sealed class ConnectDatabricksTasksCommandHandler(IDatabricksDocumentStore documents)
    : ICommandHandler<ConnectDatabricksTasksCommand>
{
    public Task<CommandResult> ExecuteAsync(ConnectDatabricksTasksCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        return DatabricksEdits.Run(documents, command.BodyPath, command, entry =>
            DatabricksEdits.JobOf(entry, command.JobKey) is { } job
                ? JobWriter.Connect(entry.Document, job, command.FromKey, command.ToKey, command.Outcome)
                : "There is no job in this file any more.");
    }
}

/// <inheritdoc cref="DisconnectDatabricksTasksCommand" />
public sealed class DisconnectDatabricksTasksCommandHandler(IDatabricksDocumentStore documents)
    : ICommandHandler<DisconnectDatabricksTasksCommand>
{
    public Task<CommandResult> ExecuteAsync(DisconnectDatabricksTasksCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        return DatabricksEdits.Run(documents, command.BodyPath, command, entry =>
            DatabricksEdits.JobOf(entry, command.JobKey) is { } job
                ? JobWriter.Disconnect(entry.Document, job, command.FromKey, command.ToKey)
                : "There is no job in this file any more.");
    }
}

/// <inheritdoc cref="RenameDatabricksTaskCommand" />
public sealed class RenameDatabricksTaskCommandHandler(IDatabricksDocumentStore documents)
    : ICommandHandler<RenameDatabricksTaskCommand>
{
    public Task<CommandResult> ExecuteAsync(RenameDatabricksTaskCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        return DatabricksEdits.Run(documents, command.BodyPath, command, entry =>
            DatabricksEdits.JobOf(entry, command.JobKey) is { } job
                ? JobWriter.RenameTask(entry.Document, job, command.TaskKey, command.NewKey)
                : "There is no job in this file any more.");
    }
}

/// <inheritdoc cref="SetDatabricksRunIfCommand" />
public sealed class SetDatabricksRunIfCommandHandler(IDatabricksDocumentStore documents)
    : ICommandHandler<SetDatabricksRunIfCommand>
{
    public Task<CommandResult> ExecuteAsync(SetDatabricksRunIfCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        return DatabricksEdits.Run(documents, command.BodyPath, command, entry =>
            DatabricksEdits.JobOf(entry, command.JobKey) is { } job
                ? JobWriter.SetRunIf(entry.Document, job, command.TaskKey, command.RunIf)
                : "There is no job in this file any more.");
    }
}

/// <inheritdoc cref="SetDatabricksClusterCommand" />
public sealed class SetDatabricksClusterCommandHandler(IDatabricksDocumentStore documents)
    : ICommandHandler<SetDatabricksClusterCommand>
{
    public Task<CommandResult> ExecuteAsync(SetDatabricksClusterCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        return DatabricksEdits.Run(documents, command.BodyPath, command, entry =>
            DatabricksEdits.JobOf(entry, command.JobKey) is { } job
                ? JobWriter.SetCluster(entry.Document, job, command.TaskKey, command.ClusterKey)
                : "There is no job in this file any more.");
    }
}
