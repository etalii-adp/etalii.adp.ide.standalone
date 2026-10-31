using EtAlii.Adp.Context;
using EtAlii.Adp.Diagram.Modules;
using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.Databricks.Tests.Parity;

/// <summary>
/// The palettes, the context menus, the property rows, the removal's confirmation and a dropped task's fresh key as
/// the module wrote them by hand before they were derived from the three DISL definitions (databricks-disl-fbl task 6,
/// Requirement 3.5), kept verbatim as the oracle the derived ones are compared with, per document, per reading and per
/// element.
/// </summary>
internal static class HandWrittenDatabricks
{
    private const string IdentityGroup = "Identity";
    private const string ExecutionGroup = "Execution";
    private const string WorkspaceGroup = "Workspace";
    private const string RenameViaMenu = "Rename through the context menu, so every depends_on reference follows the key.";
    private const string TypeIsTheFile = "The type is which *_task mapping the file carries; change it in the file.";
    private const string WorkspacePlaceholder = "Not connected — a placeholder for the real workspace connection.";
    private const string SimulationIsClientSide = "Simulated runs play on the canvas and never touch the file.";

    /// <summary>The palette per tool type, as the hand-written <c>DatabricksToolboxProvider.Items</c> listed it.</summary>
    public static IReadOnlyList<ToolboxItemDefinition> Toolbox(ToolOrigin origin) => origin.Type switch
    {
        "job" =>
        [
            new ToolboxItemDefinition(
                "databricks.toolbox.notebook-task",
                "Notebook task",
                "mdi-notebook-outline",
                "Runs a notebook. Drop on the canvas where it should sit.",
                $"{DatabricksContextActionProvider.AddTaskActionPrefix}notebook"),
            new ToolboxItemDefinition(
                "databricks.toolbox.python-task",
                "Python task",
                "mdi-language-python",
                "Runs a Python file. Drop on the canvas where it should sit.",
                $"{DatabricksContextActionProvider.AddTaskActionPrefix}python"),
            new ToolboxItemDefinition(
                "databricks.toolbox.condition-task",
                "Condition task",
                "mdi-call-split",
                "Branches on a condition; downstream edges follow its true or false outcome.",
                $"{DatabricksContextActionProvider.AddTaskActionPrefix}condition"),
        ],
        "pipeline" =>
        [
            new ToolboxItemDefinition(
                "databricks.toolbox.notebook-library",
                "Notebook library",
                "mdi-notebook-outline",
                "A notebook the pipeline's transformations come from.",
                $"{DatabricksContextActionProvider.AddLibraryActionPrefix}notebook"),
            new ToolboxItemDefinition(
                "databricks.toolbox.file-library",
                "File library",
                "mdi-file-code-outline",
                "A source file the pipeline's transformations come from.",
                $"{DatabricksContextActionProvider.AddLibraryActionPrefix}file"),
        ],
        _ =>
        [
            new ToolboxItemDefinition(
                "databricks.toolbox.job-resource",
                "Job",
                "mdi-transit-connection-horizontal",
                "A job resource skeleton, complete with a starter task.",
                $"{DatabricksContextActionProvider.AddResourceActionPrefix}jobs"),
            new ToolboxItemDefinition(
                "databricks.toolbox.pipeline-resource",
                "Pipeline",
                "mdi-pipe",
                "A pipeline resource skeleton, complete with a starter library.",
                $"{DatabricksContextActionProvider.AddResourceActionPrefix}pipelines"),
        ],
    };

    /// <summary>The context menu of <paramref name="target"/>, as <c>DatabricksContextActionProvider.Discover</c> answered it.</summary>
    public static IReadOnlyList<ContextActionGroupDefinition> Menus(ContextTarget target, DocumentEntry<LineDocument, DatabricksModel> entry)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(entry);

        if (!entry.IsUsable)
        {
            // Requirement 2.3: a file that does not parse withholds every edit.
            return [];
        }

        var model = DatabricksSelection.ReadingOf(entry.Model, target);
        var elementId = target.ElementId;
        if (DatabricksSelection.TaskOf(model, elementId) is { } selected)
        {
            return ForTask(selected.Task);
        }

        if (DatabricksSelection.TryEdge(elementId, out _, out _))
        {
            return
            [
                new ContextActionGroupDefinition(
                [
                    new ContextActionDefinition("databricks.remove-edge", "Remove dependency", "mdi-vector-polyline-remove", new ContextShortcutDefinition("Delete")),
                ]),
            ];
        }

        if (DatabricksSelection.IsBundle(elementId))
        {
            return
            [
                new ContextActionGroupDefinition(
                [
                    new ContextActionDefinition(DatabricksContextActionProvider.RenameBundleActionId, "Rename…", "mdi-pencil-outline", new ContextShortcutDefinition("F2")),
                ]),
                new ContextActionGroupDefinition(
                [
                    new ContextActionDefinition($"{DatabricksContextActionProvider.AddResourceActionPrefix}jobs", "Add job resource…", "mdi-transit-connection-horizontal"),
                    new ContextActionDefinition($"{DatabricksContextActionProvider.AddResourceActionPrefix}pipelines", "Add pipeline resource…", "mdi-pipe"),
                ]),
            ];
        }

        if (DatabricksSelection.TargetOf(model, elementId) is not null)
        {
            // The mocked domain action on a target frame (Requirement 8.4).
            return
            [
                new ContextActionGroupDefinition(
                [
                    new ContextActionDefinition(DatabricksContextActionProvider.SimulatedDeployActionId, "Deploy here (simulated)", "mdi-rocket-launch-outline"),
                ]),
            ];
        }

        if (DatabricksSelection.IsPipelineNode(elementId))
        {
            return
            [
                new ContextActionGroupDefinition(
                [
                    new ContextActionDefinition($"{DatabricksContextActionProvider.AddLibraryActionPrefix}notebook", "Add notebook library…", "mdi-notebook-outline"),
                    new ContextActionDefinition($"{DatabricksContextActionProvider.AddLibraryActionPrefix}file", "Add file library…", "mdi-file-code-outline"),
                ]),
                new ContextActionGroupDefinition(
                [
                    new ContextActionDefinition(DatabricksContextActionProvider.SimulatedUpdateActionId, "Start update (simulated)", "mdi-play-outline"),
                ]),
            ];
        }

        if (DatabricksSelection.LibraryOf(model, elementId) is not null)
        {
            return
            [
                new ContextActionGroupDefinition(
                [
                    new ContextActionDefinition(DatabricksContextActionProvider.RemoveLibraryActionId, "Remove library", "mdi-delete-outline", new ContextShortcutDefinition("Delete")),
                ]),
            ];
        }

        if (DatabricksNewPlacement.IsPlacement(elementId))
        {
            // Empty canvas discovers what can be dropped or added there, because executing an
            // action by id only finds actions its target discovers.
            return
            [
                new ContextActionGroupDefinition(
                [
                    new ContextActionDefinition($"{DatabricksContextActionProvider.AddTaskActionPrefix}notebook", "Add notebook task here", "mdi-notebook-outline"),
                    new ContextActionDefinition($"{DatabricksContextActionProvider.AddTaskActionPrefix}python", "Add Python task here", "mdi-language-python"),
                    new ContextActionDefinition($"{DatabricksContextActionProvider.AddTaskActionPrefix}condition", "Add condition task here", "mdi-call-split"),
                ]),
            ];
        }

        if (DatabricksRelationGesture.TryParse(elementId, out _, out _))
        {
            return
            [
                new ContextActionGroupDefinition(
                    [new ContextActionDefinition(DatabricksContextActionProvider.ConnectActionId, "Depend on", "mdi-ray-start-arrow")]),
            ];
        }

        return [];
    }

    /// <summary>The property rows of <paramref name="target"/>, as <c>DatabricksContextPropertyProvider.Describe</c> answered them.</summary>
    public static IReadOnlyList<ContextPropertyDefinition> Rows(ContextTarget target, DocumentEntry<LineDocument, DatabricksModel> entry)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(entry);

        var model = DatabricksSelection.ReadingOf(entry.Model, target);
        var elementId = target.ElementId;
        if (DatabricksSelection.TaskOf(model, elementId) is { } selected)
        {
            return
            [
                new ContextPropertyDefinition(DatabricksContextPropertyProvider.TaskKeyProperty, "Key", selected.Task.Key, ReadOnlyReason: RenameViaMenu, Group: IdentityGroup),
                new ContextPropertyDefinition(DatabricksContextPropertyProvider.TaskTypeProperty, "Type", selected.Task.Type, ReadOnlyReason: TypeIsTheFile, Group: IdentityGroup),
                new ContextPropertyDefinition("databricks.task-source", "Source", selected.Task.Source, ReadOnlyReason: TypeIsTheFile, Group: IdentityGroup),
                new ContextPropertyDefinition(DatabricksContextPropertyProvider.RunIfProperty, "Run if", selected.Task.RunIf, Group: ExecutionGroup),
                new ContextPropertyDefinition("databricks.cluster", "Cluster", selected.Task.ClusterKey, Group: ExecutionGroup),
                .. Placeholders(),
            ];
        }

        if (DatabricksSelection.IsBundle(elementId))
        {
            return
            [
                new ContextPropertyDefinition("databricks.bundle-name", "Name", model.Bundle.Name, Group: IdentityGroup),
                .. Placeholders(),
            ];
        }

        const string prefix = DatabricksContextPropertyProvider.PipelineScalarPrefix;
        if (DatabricksSelection.IsPipelineNode(elementId) && model.Pipelines.FirstOrDefault() is { } pipeline)
        {
            return
            [
                new ContextPropertyDefinition($"{prefix}name", "Name", pipeline.Name, Group: IdentityGroup),
                new ContextPropertyDefinition($"{prefix}catalog", "Catalog", pipeline.Catalog, Group: IdentityGroup),
                new ContextPropertyDefinition($"{prefix}schema", "Schema", pipeline.Schema, Group: IdentityGroup),
                new ContextPropertyDefinition($"{prefix}channel", "Channel", pipeline.Channel, Group: ExecutionGroup),
                .. Placeholders(),
            ];
        }

        if (DatabricksSelection.TargetOf(model, elementId) is { } frame)
        {
            return
            [
                new ContextPropertyDefinition(DatabricksContextPropertyProvider.TaskKeyProperty, "Target", frame.Name, ReadOnlyReason: "The target's key is the file's structure.", Group: IdentityGroup),
                new ContextPropertyDefinition(DatabricksContextPropertyProvider.TaskTypeProperty, "Mode", frame.Mode, ReadOnlyReason: "Change the mode in the file.", Group: IdentityGroup),
                .. Placeholders(),
            ];
        }

        return [];
    }

    /// <summary>
    /// The dependencies of the job's other tasks on the task keyed <paramref name="taskKey"/>, as the module's
    /// <c>JobWriter.ReferencesTo</c> counted them for the removal's confirmation before databricks-disl-fbl task 12 removed it.
    /// </summary>
    public static IReadOnlyList<JobDependency> ReferencesTo(JobModel job, string taskKey)
    {
        ArgumentNullException.ThrowIfNull(job);
        return job.Tasks
            .Where(task => task.Key != taskKey)
            .SelectMany(task => task.DependsOn)
            .Where(dependency => dependency.TaskKey == taskKey)
            .ToList();
    }

    /// <summary>
    /// What removing the task <paramref name="target"/> names asked first, as <c>DatabricksContextActionProvider</c>
    /// counted it: its own dependencies and every one naming it; null when there is none, or no such task.
    /// </summary>
    public static RemovalConfirmation? RemoveConfirmation(ContextTarget target, DatabricksModel model)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(model);

        if (DatabricksSelection.TaskOf(DatabricksSelection.ReadingOf(model, target), target.ElementId) is not { } selected)
        {
            return null;
        }

        var going = selected.Task.DependsOn.Count + ReferencesTo(selected.Job, selected.Task.Key).Count;
        return going == 0
            ? null
            : new RemovalConfirmation(
                "Remove task",
                going == 1
                    ? "Removing this task also removes the 1 dependency touching it."
                    : $"Removing this task also removes the {going} dependencies touching it.",
                "Remove");
    }

    /// <summary>A key no task in the job carries yet: <c>{type}_1</c>, counting up, as the hand-written <c>FreshKey</c> chose it.</summary>
    public static string FreshKey(JobModel job, string taskType)
    {
        ArgumentNullException.ThrowIfNull(job);
        for (var counter = 1; ; counter++)
        {
            var candidate = $"{taskType}_{counter}";
            if (job.Tasks.All(task => task.Key != candidate))
            {
                return candidate;
            }
        }
    }

    private static IReadOnlyList<ContextActionGroupDefinition> ForTask(JobTask task)
    {
        List<ContextActionDefinition> edits =
        [
            new(DatabricksContextActionProvider.RenameTaskActionId, "Rename…", "mdi-pencil-outline", new ContextShortcutDefinition("F2")),
            new(DatabricksContextActionProvider.SetRunIfActionId, "Set run if…", "mdi-filter-outline"),
            new(DatabricksContextActionProvider.AssignClusterActionId, "Assign cluster…", "mdi-server"),

            // One disconnect per upstream edge, so the menu names what each one severs.
            .. task.DependsOn.Select(dependency => new ContextActionDefinition(
                $"{DatabricksContextActionProvider.DisconnectActionPrefix}{dependency.TaskKey}",
                $"Disconnect from '{dependency.TaskKey}'",
                "mdi-vector-polyline-remove")),

            new(DatabricksContextActionProvider.RemoveTaskActionId, "Remove", "mdi-delete-outline", new ContextShortcutDefinition("Delete")),
        ];

        return
        [
            new ContextActionGroupDefinition(edits),
            new ContextActionGroupDefinition(
            [
                new ContextActionDefinition(DatabricksContextActionProvider.SimulatedRunActionId, "Run job (simulated)", "mdi-play-outline"),
            ]),
        ];
    }

    /// <summary>The two rows every selection wears.</summary>
    private static IEnumerable<ContextPropertyDefinition> Placeholders() =>
    [
        new(DatabricksContextPropertyProvider.WorkspaceProperty, "Workspace connection", "Not connected", ReadOnlyReason: WorkspacePlaceholder, Group: WorkspaceGroup),
        new(DatabricksContextPropertyProvider.LastSimulatedRunProperty, "Last simulated run", "", ReadOnlyReason: SimulationIsClientSide, Group: WorkspaceGroup),
    ];
}
