using EtAlii.Adp.Context;
using EtAlii.Adp.Documents.Wire;
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
public sealed class DatabricksContextActionProvider : IContextActionProvider
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

    private readonly IHistoryStackStore _historyStacks;
    private readonly IDatabricksDocumentStore _documents;

    public DatabricksContextActionProvider(IHistoryStackStore historyStacks, IDatabricksDocumentStore documents)
    {
        ArgumentNullException.ThrowIfNull(historyStacks);
        ArgumentNullException.ThrowIfNull(documents);

        _historyStacks = historyStacks;
        _documents = documents;
    }

    /// <inheritdoc />
    public ContextScope Scope => ContextScope.DiagramElement;

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<ContextActionGroupDefinition>> DiscoverAsync(ContextTarget target, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        cancellationToken.ThrowIfCancellationRequested();

        if (!DatabricksSelection.CouldBeFamilyFile(target.ResolvedFullPath))
        {
            // Another type's element; a provider consulted for every element in its scope
            // answers with nothing rather than parsing another notation's file.
            return Result([]);
        }

        var entry = _documents.GetOrLoad(target.ResolvedFullPath);
        if (!entry.IsUsable)
        {
            // Requirement 2.3: a file that does not parse withholds every edit.
            return Result([]);
        }

        if (DatabricksSelection.TaskOf(entry, target.ElementId) is { } selected)
        {
            return Result(ForTask(selected.Task));
        }

        if (DatabricksSelection.TryEdge(target.ElementId, out _, out _))
        {
            return Result(
            [
                new ContextActionGroupDefinition(
                [
                    new ContextActionDefinition(RemoveEdgeActionId, "Remove dependency", "mdi-vector-polyline-remove", new ContextShortcutDefinition("Delete")),
                ]),
            ]);
        }

        if (DatabricksSelection.IsBundle(target.ElementId))
        {
            return Result(
            [
                new ContextActionGroupDefinition(
                [
                    new ContextActionDefinition(RenameBundleActionId, "Rename…", "mdi-pencil-outline", new ContextShortcutDefinition("F2")),
                ]),
                new ContextActionGroupDefinition(
                [
                    new ContextActionDefinition($"{AddResourceActionPrefix}jobs", "Add job resource…", "mdi-transit-connection-horizontal"),
                    new ContextActionDefinition($"{AddResourceActionPrefix}pipelines", "Add pipeline resource…", "mdi-pipe"),
                ]),
            ]);
        }

        if (DatabricksSelection.TargetOf(entry, target.ElementId) is not null)
        {
            // The mocked domain action on a target frame (Requirement 8.4).
            return Result(
            [
                new ContextActionGroupDefinition(
                [
                    new ContextActionDefinition(SimulatedDeployActionId, "Deploy here (simulated)", "mdi-rocket-launch-outline"),
                ]),
            ]);
        }

        if (DatabricksSelection.IsPipelineNode(target.ElementId))
        {
            return Result(
            [
                new ContextActionGroupDefinition(
                [
                    new ContextActionDefinition($"{AddLibraryActionPrefix}notebook", "Add notebook library…", "mdi-notebook-outline"),
                    new ContextActionDefinition($"{AddLibraryActionPrefix}file", "Add file library…", "mdi-file-code-outline"),
                ]),
                new ContextActionGroupDefinition(
                [
                    new ContextActionDefinition(SimulatedUpdateActionId, "Start update (simulated)", "mdi-play-outline"),
                ]),
            ]);
        }

        if (DatabricksSelection.LibraryOf(entry, target.ElementId) is not null)
        {
            return Result(
            [
                new ContextActionGroupDefinition(
                [
                    new ContextActionDefinition(RemoveLibraryActionId, "Remove library", "mdi-delete-outline", new ContextShortcutDefinition("Delete")),
                ]),
            ]);
        }

        if (DatabricksNewPlacement.IsPlacement(target.ElementId))
        {
            // Empty canvas discovers what can be dropped or added there, because executing an
            // action by id only finds actions its target discovers.
            return Result(
            [
                new ContextActionGroupDefinition(
                [
                    new ContextActionDefinition($"{AddTaskActionPrefix}notebook", "Add notebook task here", "mdi-notebook-outline"),
                    new ContextActionDefinition($"{AddTaskActionPrefix}python", "Add Python task here", "mdi-language-python"),
                    new ContextActionDefinition($"{AddTaskActionPrefix}condition", "Add condition task here", "mdi-call-split"),
                ]),
            ]);
        }

        if (DatabricksRelationGesture.TryParse(target.ElementId, out _, out _))
        {
            return Result(
            [
                new ContextActionGroupDefinition(
                    [new ContextActionDefinition(ConnectActionId, "Depend on", "mdi-ray-start-arrow")]),
            ]);
        }

        return Result([]);
    }

    /// <inheritdoc />
    public async ValueTask<ContextExecutionResult> ExecuteAsync(ContextTarget target, string actionId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        cancellationToken.ThrowIfCancellationRequested();

        if (actionId.Contains(SimulatedMarker, StringComparison.Ordinal))
        {
            // The client plays the show; a simulated action that reaches the backend anyway
            // completes as a no-op and touches neither a file nor the history (Requirement 11.6).
            return new ContextExecutionCompleted();
        }

        var entry = _documents.GetOrLoad(target.ResolvedFullPath);
        var task = DatabricksSelection.TaskOf(entry, target.ElementId);

        switch (actionId)
        {
            case RenameTaskActionId when task is { } selected:
                return new ContextExecutionRequiresInput(new ContextInputRequest(
                    "Rename task", "mdi-pencil-outline", "Key", selected.Task.Key, "Rename", target.ElementId));

            case SetRunIfActionId when task is { } selected:
                return new ContextExecutionRequiresInput(new ContextInputRequest(
                    "Set run if", "mdi-filter-outline", "Run if (empty = ALL_SUCCESS)", selected.Task.RunIf, "Set"));

            case AssignClusterActionId when task is { } selected:
                return new ContextExecutionRequiresInput(new ContextInputRequest(
                    "Assign cluster", "mdi-server", "Cluster key (empty = serverless)", selected.Task.ClusterKey, "Assign"));

            case RemoveTaskActionId when task is { } selected:
            {
                // Requirement 2.5's shape: the action says how many edges go with it, before it
                // runs. An untouched task needs no ceremony - and no ceremony means the removal
                // happens HERE, because a Completed execution never reaches the commit leg.
                var going = selected.Task.DependsOn.Count + JobWriter.ReferencesTo(selected.Job, selected.Task.Key).Count;
                if (going == 0)
                {
                    return await DispatchAsync(target, new RemoveDatabricksTaskCommand(
                        target.ResolvedFullPath, selected.Job.Key, selected.Task.Key), cancellationToken);
                }

                return new ContextExecutionRequiresConfirmation(new ContextConfirmationRequest(
                    "Remove task",
                    "mdi-delete-outline",
                    going == 1
                        ? "Removing this task also removes the 1 dependency touching it."
                        : $"Removing this task also removes the {going} dependencies touching it.",
                    "Remove",
                    Danger: true));
            }

            case var _ when actionId.StartsWith(DisconnectActionPrefix, StringComparison.Ordinal) && task is { } selected:
                return await DispatchAsync(target, new DisconnectDatabricksTasksCommand(
                    target.ResolvedFullPath,
                    selected.Job.Key,
                    actionId[DisconnectActionPrefix.Length..],
                    selected.Task.Key), cancellationToken);

            case RemoveEdgeActionId when DatabricksSelection.TryEdge(target.ElementId, out var fromKey, out var toKey):
                return await DispatchAsync(target, new DisconnectDatabricksTasksCommand(
                    target.ResolvedFullPath, JobKeyFor(entry, toKey), fromKey, toKey), cancellationToken);

            case ConnectActionId when DatabricksRelationGesture.TryParse(target.ElementId, out var from, out var to):
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
                    target.ResolvedFullPath, JobKeyFor(entry, toKey), fromKey, toKey), cancellationToken);
            }

            case var _ when actionId.StartsWith(AddTaskActionPrefix, StringComparison.Ordinal)
                && DatabricksNewPlacement.IsPlacement(target.ElementId):
            {
                // The gesture already said everything an add needs - so nothing is asked, and
                // the task appears with a fresh key. The authored drop position is the client's
                // follow-up layout write; the computed layout places it until then.
                var job = entry.Jobs.FirstOrDefault();
                if (job is null)
                {
                    return new ContextExecutionFailed("This file declares no job to add a task to.");
                }

                var taskType = actionId[AddTaskActionPrefix.Length..];
                var key = FreshKey(job, taskType);
                return await DispatchAsync(target, new InsertDatabricksTaskCommand(
                    target.ResolvedFullPath, job.Key, key, taskType, SourceFor(taskType, key)), cancellationToken);
            }

            case var _ when actionId.StartsWith(AddTaskActionPrefix, StringComparison.Ordinal):
                return new ContextExecutionRequiresInput(new ContextInputRequest(
                    "Add task", "mdi-plus", "Task key", "", "Add"));

            case RenameBundleActionId when DatabricksSelection.IsBundle(target.ElementId):
                return new ContextExecutionRequiresInput(new ContextInputRequest(
                    "Rename bundle", "mdi-pencil-outline", "Name", entry.Bundle.Name, "Rename", target.ElementId));

            case var _ when actionId.StartsWith(AddResourceActionPrefix, StringComparison.Ordinal):
                return new ContextExecutionRequiresInput(new ContextInputRequest(
                    "Add resource", "mdi-plus", "Key", "", "Add"));

            case var _ when actionId.StartsWith(AddLibraryActionPrefix, StringComparison.Ordinal):
                return new ContextExecutionRequiresInput(new ContextInputRequest(
                    "Add library", "mdi-plus", "Path", "", "Add"));

            case RemoveLibraryActionId when DatabricksSelection.LibraryOf(entry, target.ElementId) is { } selected:
                return await DispatchAsync(target, new RemoveDatabricksLibraryCommand(
                    target.ResolvedFullPath, selected.Pipeline.Key, selected.Library.Kind, selected.Library.Path), cancellationToken);

            default:
                return new ContextExecutionCompleted();
        }
    }

    /// <inheritdoc />
    public ValueTask<ContextValidationResult> ValidateAsync(ContextTarget target, string actionId, string value, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        cancellationToken.ThrowIfCancellationRequested();

        // The writers refuse on exactly these terms at commit; validating here lets the dialog
        // refuse first (Requirement 6.4).
        if (actionId == RenameTaskActionId || actionId.StartsWith(AddTaskActionPrefix, StringComparison.Ordinal))
        {
            if (value.Trim().Length == 0)
            {
                return ValueTask.FromResult(ContextValidationResult.Rejected("A task needs a key."));
            }

            var entry = _documents.GetOrLoad(target.ResolvedFullPath);
            if (entry.Jobs.SelectMany(job => job.Tasks).Any(task => task.Key == value.Trim()))
            {
                return ValueTask.FromResult(ContextValidationResult.Rejected($"A task named '{value.Trim()}' is already there."));
            }
        }

        return ValueTask.FromResult(ContextValidationResult.Accepted);
    }

    /// <inheritdoc />
    public async ValueTask<ContextCommitResult> CommitAsync(
        ContextTarget target,
        string actionId,
        string value,
        string text,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        _ = text;

        var command = CommandFor(target, actionId, value.Trim());
        if (command is null)
        {
            return ContextCommitResult.Failed($"'{actionId}' does not apply to this selection.");
        }

        var result = await _historyStacks.Get(target.RootPath).ExecuteAsync(command, cancellationToken);
        return result.IsSuccess ? ContextCommitResult.Succeeded : ContextCommitResult.Failed(result.Error);
    }

    private ICommand? CommandFor(ContextTarget target, string actionId, string value)
    {
        var body = target.ResolvedFullPath;
        var entry = _documents.GetOrLoad(body);
        var task = DatabricksSelection.TaskOf(entry, target.ElementId);

        if (actionId.StartsWith(AddResourceActionPrefix, StringComparison.Ordinal))
        {
            return new AddDatabricksResourceCommand(body, actionId[AddResourceActionPrefix.Length..], value);
        }

        if (actionId.StartsWith(AddLibraryActionPrefix, StringComparison.Ordinal))
        {
            var pipeline = entry.Pipelines.FirstOrDefault();
            return new InsertDatabricksLibraryCommand(
                body, pipeline?.Key ?? "", actionId[AddLibraryActionPrefix.Length..], value);
        }

        if (actionId.StartsWith(AddTaskActionPrefix, StringComparison.Ordinal))
        {
            var job = entry.Jobs.FirstOrDefault();
            var taskType = actionId[AddTaskActionPrefix.Length..];
            return job is null
                ? null
                : new InsertDatabricksTaskCommand(body, job.Key, value, taskType, SourceFor(taskType, value));
        }

        return actionId switch
        {
            RenameTaskActionId when task is { } selected =>
                new RenameDatabricksTaskCommand(body, selected.Job.Key, selected.Task.Key, value),
            SetRunIfActionId when task is { } selected =>
                new SetDatabricksRunIfCommand(body, selected.Job.Key, selected.Task.Key, value),
            AssignClusterActionId when task is { } selected =>
                new SetDatabricksClusterCommand(body, selected.Job.Key, selected.Task.Key, value),
            RemoveTaskActionId when task is { } selected =>
                new RemoveDatabricksTaskCommand(body, selected.Job.Key, selected.Task.Key),
            RenameBundleActionId => new SetDatabricksBundleNameCommand(body, value),
            _ => null,
        };
    }

    private async ValueTask<ContextExecutionResult> DispatchAsync(ContextTarget target, ICommand command, CancellationToken cancellationToken)
    {
        var result = await _historyStacks.Get(target.RootPath).ExecuteAsync(command, cancellationToken);
        return result.IsSuccess
            ? new ContextExecutionCompleted()
            : new ContextExecutionFailed(result.Error);
    }

    private static IReadOnlyList<ContextActionGroupDefinition> ForTask(JobTask task)
    {
        List<ContextActionDefinition> edits =
        [
            new(RenameTaskActionId, "Rename…", "mdi-pencil-outline", new ContextShortcutDefinition("F2")),
            new(SetRunIfActionId, "Set run if…", "mdi-filter-outline"),
            new(AssignClusterActionId, "Assign cluster…", "mdi-server"),

            // One disconnect per upstream edge, so the menu names what each one severs.
            .. task.DependsOn.Select(dependency => new ContextActionDefinition(
                $"{DisconnectActionPrefix}{dependency.TaskKey}",
                $"Disconnect from '{dependency.TaskKey}'",
                "mdi-vector-polyline-remove")),

            new(RemoveTaskActionId, "Remove", "mdi-delete-outline", new ContextShortcutDefinition("Delete")),
        ];

        return
        [
            new ContextActionGroupDefinition(edits),
            new ContextActionGroupDefinition(
            [
                new ContextActionDefinition(SimulatedRunActionId, "Run job (simulated)", "mdi-play-outline"),
            ]),
        ];
    }

    /// <summary>The job whose task carries <paramref name="taskKey"/>; empty for the file's first.</summary>
    private static string JobKeyFor(DatabricksDocumentEntry entry, string taskKey) =>
        entry.Jobs.FirstOrDefault(job => job.Tasks.Any(task => task.Key == taskKey))?.Key ?? "";

    /// <summary>An element id's task key: <c>task:{key}</c>, or the id as given.</summary>
    private static string KeyOf(string elementId) =>
        elementId.StartsWith("task:", StringComparison.Ordinal) ? elementId["task:".Length..] : elementId;

    /// <summary>A key no task in the job carries yet: <c>{type}_1</c>, counting up.</summary>
    private static string FreshKey(JobModel job, string taskType)
    {
        for (var counter = 1; ; counter++)
        {
            var candidate = $"{taskType}_{counter}";
            if (job.Tasks.All(task => task.Key != candidate))
            {
                return candidate;
            }
        }
    }

    private static string SourceFor(string taskType, string key) => taskType switch
    {
        "notebook" => $"notebooks/{key}",
        "python" => $"scripts/{key}.py",
        _ => key,
    };

    private static ValueTask<IReadOnlyList<ContextActionGroupDefinition>> Result(IReadOnlyList<ContextActionGroupDefinition> groups) =>
        ValueTask.FromResult(groups);
}
