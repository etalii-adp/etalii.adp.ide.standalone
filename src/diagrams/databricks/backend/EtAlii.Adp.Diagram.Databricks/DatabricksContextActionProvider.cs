using EtAlii.Adp.Context;
using EtAlii.Adp.Diagram.Modules;
using EtAlii.Adp.Documents;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.Databricks;

/// <summary>
/// What can be done to a selected Databricks element, offered as data and reached through the
/// one path the context service defines (databricks-diagrams Requirement 11).
/// </summary>
/// <remarks>
/// <para>
/// Every real edit dispatches a command through the project's history, so each is one undo away
/// like every other edit in the IDE. The simulated entries are the exception by design
/// (Requirement 8): they discover carrying the <c>.simulated.</c> marker in their id - the seam
/// the client intercepts - and execute here as a no-op <c>Completed</c>, so a simulation that
/// reaches the backend anyway (the ribbon, a stale menu) still never touches a file or the
/// history (Requirement 11.6).
/// </para>
/// <para>
/// The relation gesture arrives whole in one <c>rel:{from}-&gt;{to}</c> id and a drop's landing
/// point in a <c>new:{x},{y}</c> id - the timeline's stateless mechanisms, reused because the
/// context channel carries one element id per call and nothing else.
/// </para>
/// </remarks>
public sealed class DatabricksContextActionProvider : DiagramActionProvider<LineDocument, DatabricksModel>
{
    /// <summary>Rename a task, rewriting every reference with it (Requirement 11.4).</summary>
    public const string RenameTaskActionId = "databricks.rename-task";

    /// <summary>Set a task's run_if, asking for the value first.</summary>
    public const string SetRunIfActionId = "databricks.set-run-if";

    /// <summary>Bind a task to a declared cluster, asking for the key first; empty means serverless.</summary>
    public const string AssignClusterActionId = "databricks.assign-cluster";

    /// <summary>Remove a task and every edge touching it, as one undo (Requirement 11.5).</summary>
    public const string RemoveTaskActionId = "databricks.remove-task";

    /// <summary>The prefix of a per-dependency disconnect: <c>databricks.disconnect:{fromKey}</c>.</summary>
    public const string DisconnectActionPrefix = "databricks.disconnect:";

    /// <summary>Remove a selected dependency edge.</summary>
    private const string RemoveEdgeActionId = "databricks.remove-edge";

    /// <summary>The relation gesture, whole in one call.</summary>
    public const string ConnectActionId = "databricks.connect";

    /// <summary>Add a task at a placement or via a key dialog: <c>databricks.add-task:{type}</c>.</summary>
    public const string AddTaskActionPrefix = "databricks.add-task:";

    /// <summary>Rename the bundle.</summary>
    public const string RenameBundleActionId = "databricks.rename-bundle";

    /// <summary>Add a resource skeleton: <c>databricks.add-resource:{kind}</c>.</summary>
    public const string AddResourceActionPrefix = "databricks.add-resource:";

    /// <summary>Add a library to the pipeline: <c>databricks.add-library:{kind}</c>.</summary>
    public const string AddLibraryActionPrefix = "databricks.add-library:";

    /// <summary>Remove a selected library entry.</summary>
    public const string RemoveLibraryActionId = "databricks.remove-library";

    /// <summary>
    /// The simulated actions (Requirement 8): the marker every one of their ids carries, which
    /// is what the client's canvas intercepts before <c>executeAction</c> - and what tells
    /// everything after the interception that no file and no history may be touched.
    /// </summary>
    public const string SimulatedMarker = ".simulated.";

    /// <summary>Run the job as a mock (Requirement 8.1).</summary>
    public const string SimulatedRunActionId = "databricks.simulated.run-job";

    /// <summary>Deploy the bundle to the selected target as a mock (Requirement 8.4).</summary>
    public const string SimulatedDeployActionId = "databricks.simulated.deploy";

    /// <summary>Start a pipeline update as a mock (Requirement 8.5).</summary>
    public const string SimulatedUpdateActionId = "databricks.simulated.pipeline-update";

    public DatabricksContextActionProvider(IHistoryStackStore historyStacks, IDatabricksDocumentStore documents)
        : base(historyStacks, documents)
    {
    }

    /// <inheritdoc />
    /// <remarks>Another type's element is answered with nothing rather than by parsing another notation's file.</remarks>
    protected override bool IsBody(string path) => DatabricksSelection.CouldBeFamilyFile(path);

    /// <inheritdoc />
    /// <remarks>
    /// Derived from the definition of the reading that holds the target (<see cref="DatabricksDefinition.Menus"/>), of
    /// the resource the target's registration names (finding B3); nothing on a file that does not parse
    /// (Requirement 2.3). Executing an action by id only finds actions its target discovers, so the empty canvas
    /// discovers what can be dropped or added there.
    /// </remarks>
    protected override IReadOnlyList<ContextActionGroupDefinition> Discover(ContextTarget target, DocumentEntry<LineDocument, DatabricksModel> entry) =>
        DatabricksDefinition.Menus(target, entry);

    /// <inheritdoc />
    protected override async ValueTask<ContextExecutionResult> RunAsync(
        ContextTarget target,
        DocumentEntry<LineDocument, DatabricksModel> entry,
        string actionId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(entry);

        if (actionId.Contains(SimulatedMarker, StringComparison.Ordinal))
        {
            // The client plays the show; a simulated action that reaches the backend anyway
            // completes as a no-op and touches neither a file nor the history (Requirement 11.6).
            return new ContextExecutionCompleted();
        }

        // The resource the target's registration names, not the file's first (finding B3).
        var model = DatabricksSelection.ReadingOf(entry.Model, target);
        var task = DatabricksSelection.TaskOf(model, target.ElementId);

        return actionId switch
        {
            RenameTaskActionId or SetRunIfActionId or AssignClusterActionId when task is { } selected =>
                AskAboutTask(target, actionId, selected.Task),
            RemoveTaskActionId when task is { } selected =>
                await RemoveTaskAsync(target, entry, selected.Job, selected.Task, cancellationToken),
            _ when actionId.StartsWith(DisconnectActionPrefix, StringComparison.Ordinal) && task is { } selected =>
                await DisconnectAsync(target, actionId, selected.Job, selected.Task, cancellationToken),
            RemoveEdgeActionId when DatabricksSelection.TryEdge(target.ElementId, out var fromKey, out var toKey) =>
                await DispatchAsync(target, new DisconnectDatabricksTasksCommand(
                    target.ResolvedFullPath, JobKeyFor(model, target, toKey), fromKey, toKey), cancellationToken),
            ConnectActionId when DatabricksRelationGesture.TryParse(target.ElementId, out var from, out var to) =>
                await ConnectAsync(target, model, from, to, cancellationToken),
            _ when actionId.StartsWith(AddTaskActionPrefix, StringComparison.Ordinal)
                && DatabricksNewPlacement.IsPlacement(target.ElementId) =>
                await AddTaskAtPlacementAsync(target, model, actionId, cancellationToken),
            _ when actionId.StartsWith(AddTaskActionPrefix, StringComparison.Ordinal) =>
                AskForNew("Add task", "Task key"),
            RenameBundleActionId when DatabricksSelection.IsBundle(target.ElementId) =>
                new ContextExecutionRequiresInput(new ContextInputRequest(
                    "Rename bundle", "mdi-pencil-outline", "Name", model.Bundle.Name, "Rename", target.ElementId)),
            _ when actionId.StartsWith(AddResourceActionPrefix, StringComparison.Ordinal) =>
                AskForNew("Add resource", "Key"),
            _ when actionId.StartsWith(AddLibraryActionPrefix, StringComparison.Ordinal) =>
                AskForNew("Add library", "Path"),
            RemoveLibraryActionId when DatabricksSelection.LibraryOf(model, target.ElementId) is { } selected =>
                await DispatchAsync(target, new RemoveDatabricksLibraryCommand(
                    target.ResolvedFullPath, selected.Pipeline.Key, selected.Library.Kind, selected.Library.Path), cancellationToken),
            _ => new ContextExecutionCompleted(),
        };
    }

    /// <summary>Rename a task, set its run_if or assign its cluster: each asks for the value first.</summary>
    private static ContextExecutionRequiresInput AskAboutTask(ContextTarget target, string actionId, JobTask task) =>
        new(actionId switch
        {
            RenameTaskActionId => new ContextInputRequest(
                "Rename task", "mdi-pencil-outline", "Key", task.Key, "Rename", target.ElementId),
            SetRunIfActionId => new ContextInputRequest(
                "Set run if", "mdi-filter-outline", "Run if (empty = ALL_SUCCESS)", task.RunIf, "Set"),
            _ => new ContextInputRequest(
                "Assign cluster", "mdi-server", "Cluster key (empty = serverless)", task.ClusterKey, "Assign"),
        });

    /// <summary>Add something through a dialog, asking for its <paramref name="field"/> first.</summary>
    private static ContextExecutionRequiresInput AskForNew(string title, string field) =>
        new(new ContextInputRequest(title, "mdi-plus", field, "", "Add"));

    /// <summary>Remove a task and every edge touching it, confirming first when there are any.</summary>
    private async ValueTask<ContextExecutionResult> RemoveTaskAsync(
        ContextTarget target, DocumentEntry<LineDocument, DatabricksModel> entry, JobModel job, JobTask task, CancellationToken cancellationToken)
    {
        // Requirement 2.5's shape: the definition's confirmation says how many edges go with it,
        // before it runs. An untouched task needs no ceremony - and no ceremony means the removal
        // happens HERE, because a Completed execution never reaches the commit leg.
        return await RemoveAsync(
            target,
            new RemoveDatabricksTaskCommand(target.ResolvedFullPath, job.Key, task.Key),
            DatabricksDefinition.RemoveConfirmation(target, entry.Model),
            cancellationToken);
    }

    /// <summary>Remove the selected task's dependency on the task the action id names: <c>databricks.disconnect:{fromKey}</c>.</summary>
    private async ValueTask<ContextExecutionResult> DisconnectAsync(
        ContextTarget target, string actionId, JobModel job, JobTask task, CancellationToken cancellationToken) =>
        await DispatchAsync(target, new DisconnectDatabricksTasksCommand(
            target.ResolvedFullPath,
            job.Key,
            actionId[DisconnectActionPrefix.Length..],
            task.Key), cancellationToken);

    /// <summary>A finished relation gesture, from <paramref name="from"/> to <paramref name="to"/>.</summary>
    private async ValueTask<ContextExecutionResult> ConnectAsync(
        ContextTarget target, DatabricksModel model, string from, string to, CancellationToken cancellationToken)
    {
        // The whole gesture in one call. This family creates tasks by drop, not by
        // relation-to-empty-space, so either end being a placement is a polite refusal
        // rather than a create-and-connect.
        if (DatabricksNewPlacement.IsPlacement(from) || DatabricksNewPlacement.IsPlacement(to))
        {
            return new ContextExecutionFailed("Drop the dependency on a task; a dependency needs both of its ends.");
        }

        var fromKey = KeyOf(from);
        var toKey = KeyOf(to);
        return await DispatchAsync(target, new ConnectDatabricksTasksCommand(
            target.ResolvedFullPath, JobKeyFor(model, target, toKey), fromKey, toKey), cancellationToken);
    }

    /// <summary>A task dropped on a placement: added at once, with the key the definition's drop gives it.</summary>
    private async ValueTask<ContextExecutionResult> AddTaskAtPlacementAsync(
        ContextTarget target, DatabricksModel model, string actionId, CancellationToken cancellationToken)
    {
        // The gesture already said everything an add needs - so nothing is asked, and
        // the task appears with the key the definition's drop gives it (finding A11). The
        // authored drop position is the client's follow-up layout write; the computed
        // layout places it until then.
        var job = model.Jobs.FirstOrDefault();
        if (job is null)
        {
            return new ContextExecutionFailed("This file declares no job to add a task to.");
        }

        var taskType = actionId[AddTaskActionPrefix.Length..];
        var key = DatabricksDefinition.DroppedTaskKey(job, actionId);
        return await DispatchAsync(target, new InsertDatabricksTaskCommand(
            target.ResolvedFullPath, job.Key, key, taskType, SourceFor(taskType, key)), cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>The writers refuse on exactly these terms at commit; validating here lets the dialog refuse first (Requirement 6.4).</remarks>
    protected override ContextValidationResult Validate(ContextTarget target, DocumentEntry<LineDocument, DatabricksModel> entry, string actionId, string value)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(value);

        if (actionId == RenameTaskActionId || actionId.StartsWith(AddTaskActionPrefix, StringComparison.Ordinal))
        {
            if (value.Trim().Length == 0)
            {
                return ContextValidationResult.Rejected("A task needs a key.");
            }

            if (entry.Model.Jobs.SelectMany(job => job.Tasks).Any(task => task.Key == value.Trim()))
            {
                return ContextValidationResult.Rejected($"A task named '{value.Trim()}' is already there.");
            }
        }

        return ContextValidationResult.Accepted;
    }

    /// <inheritdoc />
    protected override ICommand? CommitCommand(ContextTarget target, DatabricksModel model, string actionId, string value)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(value);

        var trimmed = value.Trim();
        var body = target.ResolvedFullPath;
        var reading = DatabricksSelection.ReadingOf(model, target);
        var task = DatabricksSelection.TaskOf(reading, target.ElementId);

        if (actionId.StartsWith(AddResourceActionPrefix, StringComparison.Ordinal))
        {
            return new AddDatabricksResourceCommand(body, actionId[AddResourceActionPrefix.Length..], trimmed);
        }

        if (actionId.StartsWith(AddLibraryActionPrefix, StringComparison.Ordinal))
        {
            var pipeline = reading.Pipelines.FirstOrDefault();
            return new InsertDatabricksLibraryCommand(
                body, pipeline?.Key ?? DatabricksSelection.ResourceKeyOf(target), actionId[AddLibraryActionPrefix.Length..], trimmed);
        }

        if (actionId.StartsWith(AddTaskActionPrefix, StringComparison.Ordinal))
        {
            var job = reading.Jobs.FirstOrDefault();
            var taskType = actionId[AddTaskActionPrefix.Length..];
            return job is null
                ? null
                : new InsertDatabricksTaskCommand(body, job.Key, trimmed, taskType, SourceFor(taskType, trimmed));
        }

        return actionId switch
        {
            RenameTaskActionId when task is { } selected =>
                KeepingPosition(target, new RenameDatabricksTaskCommand(body, selected.Job.Key, selected.Task.Key, trimmed)),
            SetRunIfActionId when task is { } selected =>
                new SetDatabricksRunIfCommand(body, selected.Job.Key, selected.Task.Key, trimmed),
            AssignClusterActionId when task is { } selected =>
                new SetDatabricksClusterCommand(body, selected.Job.Key, selected.Task.Key, trimmed),
            RemoveTaskActionId when task is { } selected =>
                new RemoveDatabricksTaskCommand(body, selected.Job.Key, selected.Task.Key),
            RenameBundleActionId => new SetDatabricksBundleNameCommand(body, trimmed),
            _ => null,
        };
    }

    /// <summary>
    /// The job of <paramref name="model"/> whose task carries <paramref name="taskKey"/>, else its first -
    /// and where it has none, the job <paramref name="target"/>'s registration names, so a command never
    /// falls through to another job of the file (finding B3).
    /// </summary>
    private static string JobKeyFor(DatabricksModel model, ContextTarget target, string taskKey) =>
        model.Jobs.FirstOrDefault(job => job.Tasks.Any(task => task.Key == taskKey))?.Key
        ?? model.Jobs.FirstOrDefault()?.Key
        ?? DatabricksSelection.ResourceKeyOf(target);

    /// <summary>An element id's task key: <c>task:{key}</c>, or the id as given.</summary>
    private static string KeyOf(string elementId) =>
        elementId.StartsWith("task:", StringComparison.Ordinal) ? elementId["task:".Length..] : elementId;

    /// <summary>
    /// A rename with the move of the renamed task's stored position from its old id to its new one, as one step of the
    /// history (finding B16, Requirement 6.6): the rename alone when the target's registration stores no position for it.
    /// </summary>
    private static ICommand KeepingPosition(ContextTarget target, RenameDatabricksTaskCommand rename)
    {
        var oldId = $"task:{rename.TaskKey}";
        var newId = $"task:{rename.NewKey}";
        if (rename.NewKey.Length == 0 || oldId == newId || target.RegistrationPath is not { Length: > 0 } registration || !File.Exists(registration)
            || !RegistrationLayout.Read(registration).TryGetValue(oldId, out var stored))
        {
            return rename;
        }

        return new DatabricksStepsCommand(
        [
            rename,
            new CarryDatabricksPositionCommand(registration, oldId, newId, stored.X, stored.Y),
        ]);
    }

    private static string SourceFor(string taskType, string key) => taskType switch
    {
        "notebook" => $"notebooks/{key}",
        "python" => $"scripts/{key}.py",
        _ => key,
    };
}
