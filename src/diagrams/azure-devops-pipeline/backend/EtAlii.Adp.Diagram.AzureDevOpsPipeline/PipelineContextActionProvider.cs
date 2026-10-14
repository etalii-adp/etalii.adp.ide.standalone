using EtAlii.Adp.Context;
using EtAlii.Adp.Documents;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.AzureDevOpsPipeline;

/// <summary>
/// What a user can do to a pipeline element, offered the way every other action is - so it reaches
/// the ribbon, the right-click menu and the keyboard through one path, and the client holds no
/// key-to-action table of its own.
/// </summary>
/// <remarks>
/// <para>
/// An action that would fail is not offered. That is the whole discipline here: the commands
/// refuse an edit that would produce a pipeline Azure DevOps will not run, and this asks the same
/// questions first, so a user never reaches for something that then tells them no. A stage that is
/// the only one, a job that is its stage's last, a step whose job would be left empty - none of
/// them offer Remove at all (Requirement 9.7).
/// </para>
/// <para>
/// Elements from a template offer nothing. Their text lives in another file, so every edit here
/// would land in the wrong one (Requirement 5.4), and greying the actions out would still invite
/// the click.
/// </para>
/// </remarks>
public sealed class PipelineContextActionProvider : DiagramActionProvider<LineDocument, PipelineModel>
{
    /// <summary>Sets the element's <c>displayName</c>.</summary>
    public const string RenameActionId = "azure-pipeline.rename";

    /// <summary>Turns <c>enabled: false</c> on or off.</summary>
    public const string ToggleEnabledActionId = "azure-pipeline.toggle-enabled";

    /// <summary>Adds a stage after this one.</summary>
    public const string AddStageActionId = "azure-pipeline.add-stage";

    /// <summary>Adds a job to this stage.</summary>
    public const string AddJobActionId = "azure-pipeline.add-job";

    /// <summary>Adds a deployment job to this stage.</summary>
    public const string AddDeploymentJobActionId = "azure-pipeline.add-deployment-job";

    /// <summary>Adds a script step to this job.</summary>
    public const string AddStepActionId = "azure-pipeline.add-step";

    /// <summary>Removes this element.</summary>
    public const string RemoveActionId = "azure-pipeline.remove";

    /// <summary>Moves this step one place earlier in its job.</summary>
    public const string MoveStepUpActionId = "azure-pipeline.move-step-up";

    /// <summary>Moves this step one place later in its job.</summary>
    public const string MoveStepDownActionId = "azure-pipeline.move-step-down";

    /// <summary>Puts the element's <c>dependsOn</c> back on the schema's default.</summary>
    public const string ClearDependenciesActionId = "azure-pipeline.clear-dependencies";

    /// <summary>Shows a stage's jobs, or hides them again.</summary>
    private const string ToggleStageActionId = "azure-pipeline.toggle-stage";

    /// <summary>Opens or closes a job, showing or hiding its steps (Requirement 8.2).</summary>
    public const string ToggleJobActionId = "azure-pipeline.toggle-job";

    private const string Gone = "That element is no longer in this pipeline.";

    private readonly PipelineViewState _views;

    /// <summary>Creates the provider.</summary>
    /// <remarks>A pipeline is read from the target's workspace root as well as its path, for its templates.</remarks>
    public PipelineContextActionProvider(
        IHistoryStackStore historyStacks,
        IPipelineDocumentStore documents,
        PipelineViewState views)
        : base(historyStacks, IPipelineDocumentStore.LoaderFor(documents))
    {
        ArgumentNullException.ThrowIfNull(views);
        _views = views;
    }

    /// <inheritdoc />
    protected override bool IsBody(string path) => Diagram.IsPipelineFile(path);

    /// <inheritdoc />
    protected override bool Serves(ContextTarget target) => base.Serves(target) && target.ElementId.Length > 0;

    /// <inheritdoc />
    /// <remarks>What the element offers whatever is open; <see cref="Discover(ContextTarget, DocumentEntry{LineDocument, PipelineModel})"/> adds the opening.</remarks>
    protected override IReadOnlyList<ContextActionGroupDefinition> Discover(DocumentEntry<LineDocument, PipelineModel> entry, string elementId)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (Resolve(entry, elementId) is not { } location || !location.Target.IsEditable)
        {
            // Nothing may be edited in a file that does not parse, and an element whose text is
            // somewhere else has nothing here to do to it.
            return [];
        }

        var edits = new List<ContextActionDefinition>
        {
            new(RenameActionId, "Rename…", "mdi-pencil-outline", new ContextShortcutDefinition("F2")),
            new(
                ToggleEnabledActionId,
                Execution(location).IsDisabled ? "Enable" : "Disable",
                Execution(location).IsDisabled ? "mdi-play-circle-outline" : "mdi-pause-circle-outline"),
        };

        if (location.Kind != PipelineElementLocationKind.Step && DependsOnDeclared(location))
        {
            // Only worth offering when there is something to clear: a stage with no dependsOn is
            // already on the default.
            edits.Add(new ContextActionDefinition(
                ClearDependenciesActionId,
                "Use the default order",
                "mdi-arrow-decision-outline"));
        }

        if (Removable(entry.Model, location))
        {
            edits.Add(new ContextActionDefinition(
                RemoveActionId,
                "Remove",
                "mdi-delete-outline",
                new ContextShortcutDefinition("Delete")));
        }

        var additions = AdditionsFor(location);
        var ordering = OrderingFor(location);

        // Three groups, so the menu separates changing this element from adding another and from
        // moving it - an empty group would draw a separator with nothing under it.
        return new[]
            {
                new ContextActionGroupDefinition(edits),
                additions.Count > 0 ? new ContextActionGroupDefinition(additions) : null,
                ordering.Count > 0 ? new ContextActionGroupDefinition(ordering) : null,
            }
            .OfType<ContextActionGroupDefinition>()
            .ToArray();
    }

    /// <inheritdoc />
    /// <remarks>
    /// Opening a stage or a job is how what is inside it is ever seen, so it leads what can be done
    /// to one, and only where there is something inside to show (Requirement 8.2). Whether it is
    /// open belongs to the connection, which is why it is added here rather than with the edits.
    /// </remarks>
    protected override IReadOnlyList<ContextActionGroupDefinition> Discover(ContextTarget target, DocumentEntry<LineDocument, PipelineModel> entry)
    {
        ArgumentNullException.ThrowIfNull(target);

        var groups = Discover(entry, target.ElementId);
        if (groups.Count == 0 || Resolve(entry, target.ElementId) is not { } location || OpenerOf(location) is not { } inside)
        {
            return groups;
        }

        var open = _views.For(target.WatchId, target.ResolvedFullPath).IsExpanded(inside.Id);
        var toggle = new ContextActionDefinition(
            inside.ActionId,
            open ? inside.Hide : inside.Show,
            open ? "mdi-unfold-less-horizontal" : "mdi-unfold-more-horizontal",
            new ContextShortcutDefinition(" "));

        return [new ContextActionGroupDefinition([toggle, .. groups[0].Actions]), .. groups.Skip(1)];
    }

    /// <inheritdoc />
    protected override async ValueTask<ContextExecutionResult> RunAsync(
        ContextTarget target,
        DocumentEntry<LineDocument, PipelineModel> entry,
        string actionId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(entry);

        if (Resolve(entry, target.ElementId) is not { } location)
        {
            return new ContextExecutionFailed(Gone);
        }

        if (actionId == ToggleStageActionId || actionId == ToggleJobActionId)
        {
            // View state, not a command: nothing is written and nothing lands on the history,
            // because which stages this connection has open is not a fact about the pipeline.
            // Toggled through the view state's announcing method, which is what makes the session
            // push the deltas - toggling a view directly would change state no client hears of.
            _views.Toggle(
                target.WatchId,
                target.ResolvedFullPath,
                actionId == ToggleJobActionId ? location.Job!.Id : location.Stage.Id);
            return new ContextExecutionCompleted();
        }

        if (actionId == RenameActionId)
        {
            // A rename asks for the new text first.
            return new ContextExecutionRequiresInput(new ContextInputRequest(
                "Rename",
                "mdi-pencil-outline",
                "Display name",
                DisplayName(location),
                "Rename",
                target.ElementId));
        }

        // Everything else has all it needs already, so it runs now rather than putting a dialog in
        // the way of one keystroke - through the project's history, so every one of these is one
        // undo away like every other edit in the IDE (Requirement 9.8).
        return CommandFor(target, actionId, "", location) is { } command
            ? await DispatchAsync(target, command, cancellationToken)
            : new ContextExecutionFailed($"'{actionId}' does not apply to this selection.");
    }

    /// <inheritdoc />
    protected override ICommand? CommitCommand(ContextTarget target, PipelineModel model, string actionId, string value) =>
        PipelineEdits.Locate(model, target.ElementId) is { } location
            ? CommandFor(target, actionId, value, location)
            : null;

    private static ICommand? CommandFor(ContextTarget target, string actionId, string value, PipelineElementLocation location)
    {
        var root = target.RootPath;
        var body = target.ResolvedFullPath;
        var id = location.Id;

        return actionId switch
        {
            RenameActionId => new RenamePipelineElementCommand(root, body, id, value),
            ToggleEnabledActionId => new SetPipelineElementEnabledCommand(root, body, id, Execution(location).IsDisabled),
            ClearDependenciesActionId => new SetPipelineDependenciesCommand(root, body, id, [], Declared: false),
            RemoveActionId => new RemovePipelineElementCommand(root, body, id),
            // The element itself is the parent, not the stage or job enclosing it. The menu only
            // offers each of these where it belongs, and a toolbox drop goes through this same
            // path - so passing the actual target lets the add command judge it and say the useful
            // thing ("a step goes in a job") rather than this quietly retargeting the drop at
            // whatever ancestor would have accepted it.
            AddStageActionId => new AddPipelineElementCommand(root, body, PipelineAddKind.Stage, ""),
            AddJobActionId => new AddPipelineElementCommand(root, body, PipelineAddKind.Job, id),
            AddDeploymentJobActionId => new AddPipelineElementCommand(root, body, PipelineAddKind.DeploymentJob, id),
            AddStepActionId => new AddPipelineElementCommand(root, body, PipelineAddKind.Step, id),
            MoveStepUpActionId => new MovePipelineStepCommand(root, body, id, IndexOf(location) - 1),
            MoveStepDownActionId => new MovePipelineStepCommand(root, body, id, IndexOf(location) + 1),
            _ => null,
        };
    }

    /// <summary>What can be added here, which depends entirely on what "here" is.</summary>
    /// <remarks>
    /// A stage goes beside a stage, a job in a stage, a step in a job. Offering an add where it
    /// does not belong would put the refusal in the command rather than in the menu, which is one
    /// click too late.
    /// </remarks>
    private static List<ContextActionDefinition> AdditionsFor(PipelineElementLocation location) => location.Kind switch
    {
        PipelineElementLocationKind.Stage =>
        [
            new ContextActionDefinition(AddStageActionId, "Add stage", "mdi-layers-plus"),
            new ContextActionDefinition(AddJobActionId, "Add job", "mdi-plus-box-outline"),
            new ContextActionDefinition(AddDeploymentJobActionId, "Add deployment job", "mdi-rocket-launch-outline"),
        ],
        PipelineElementLocationKind.Job => [new ContextActionDefinition(AddStepActionId, "Add step", "mdi-plus-box-outline")],
        _ => [],
    };

    /// <summary>
    /// Moving a step within its job - the one thing in a pipeline whose order is its meaning.
    /// </summary>
    /// <remarks>
    /// Withheld at the ends: a first step has nowhere to move up to. Withheld entirely where any
    /// of the job's steps came from a template, because then the order on the canvas is not the
    /// order in this file and moving by position would move the wrong lines.
    ///
    /// The shortcuts are Alt+Up and Alt+Down in the shape a <c>KeyboardEvent</c> reports the press:
    /// the key <c>ArrowUp</c> with Alt held. The text <c>"Alt+Up"</c> as a key matched no keypress.
    /// </remarks>
    private static List<ContextActionDefinition> OrderingFor(PipelineElementLocation location)
    {
        if (location.Kind != PipelineElementLocationKind.Step || location.Job!.Steps.Any(step => step.IsFromTemplate))
        {
            return [];
        }

        var index = IndexOf(location);
        var actions = new List<ContextActionDefinition>();
        if (index > 0)
        {
            actions.Add(new ContextActionDefinition(
                MoveStepUpActionId, "Move up", "mdi-arrow-up", new ContextShortcutDefinition("ArrowUp", Alt: true)));
        }

        if (index < location.Job.Steps.Count - 1)
        {
            actions.Add(new ContextActionDefinition(
                MoveStepDownActionId, "Move down", "mdi-arrow-down", new ContextShortcutDefinition("ArrowDown", Alt: true)));
        }

        return actions;
    }

    /// <summary>
    /// Whether removing this element would leave a pipeline that still runs.
    /// </summary>
    /// <remarks>
    /// The same questions <see cref="RemovePipelineElementCommandHandler"/> asks, asked earlier so
    /// the answer is a missing menu item rather than an error message.
    /// </remarks>
    private static bool Removable(PipelineModel model, PipelineElementLocation location) => location.Kind switch
    {
        PipelineElementLocationKind.Stage => model.Stages.Count(stage => !stage.IsImplicit) > 1,
        PipelineElementLocationKind.Job => location.Stage.Jobs.Count > 1,
        _ => location.Job!.Steps.Count > 1,
    };

    private static PipelineExecution Execution(PipelineElementLocation location) => location.Kind switch
    {
        PipelineElementLocationKind.Stage => location.Stage.Execution,
        PipelineElementLocationKind.Job => location.Job!.Execution,
        _ => location.Step!.Execution,
    };

    private static string DisplayName(PipelineElementLocation location) => location.Kind switch
    {
        PipelineElementLocationKind.Stage => location.Stage.DisplayName,
        PipelineElementLocationKind.Job => location.Job!.DisplayName,
        _ => location.Step!.DisplayName,
    };

    private static bool DependsOnDeclared(PipelineElementLocation location) =>
        location.Kind == PipelineElementLocationKind.Stage
            ? location.Stage.DependsOnDeclared
            : location.Job!.DependsOnDeclared;

    private static int IndexOf(PipelineElementLocation location) =>
        location.Job!.Steps.ToList().FindIndex(step => step.Id == location.Step!.Id);

    /// <summary>The element <paramref name="elementId"/> names in a document that was read, or null.</summary>
    private static PipelineElementLocation? Resolve(DocumentEntry<LineDocument, PipelineModel> entry, string elementId) =>
        entry.IsUsable && elementId.Length > 0 ? PipelineEdits.Locate(entry.Model, elementId) : null;

    /// <summary>What a stage or a job opens to show, or null where there is nothing inside it to show.</summary>
    private static PipelineOpener? OpenerOf(PipelineElementLocation location) => location switch
    {
        { Kind: PipelineElementLocationKind.Stage, Stage.Jobs.Count: > 0 } =>
            new PipelineOpener(location.Stage.Id, ToggleStageActionId, "Show jobs", "Hide jobs"),
        { Kind: PipelineElementLocationKind.Job } when location.Job!.Steps.Count > 0 =>
            new PipelineOpener(location.Job.Id, ToggleJobActionId, "Show steps", "Hide steps"),
        _ => null,
    };

    /// <summary>What opening an element shows it by: its id in the view state, the action, and its two labels.</summary>
    private sealed record PipelineOpener(string Id, string ActionId, string Show, string Hide);
}
