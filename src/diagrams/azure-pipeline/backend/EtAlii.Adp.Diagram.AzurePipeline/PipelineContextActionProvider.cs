using EtAlii.Adp.Common;
using EtAlii.Adp.Common.Wire;
namespace EtAlii.Adp.Diagram.AzurePipeline;

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
public sealed class PipelineContextActionProvider : IContextActionProvider
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
    public const string ToggleStageActionId = "azure-pipeline.toggle-stage";

    private const string Gone = "That element is no longer in this pipeline.";

    private readonly IHistoryStackStore _historyStacks;
    private readonly IPipelineDocumentStore _documents;
    private readonly PipelineViewState _views;

    /// <summary>Creates the provider.</summary>
    public PipelineContextActionProvider(
        IHistoryStackStore historyStacks,
        IPipelineDocumentStore documents,
        PipelineViewState views)
    {
        ArgumentNullException.ThrowIfNull(historyStacks);
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(views);
        _historyStacks = historyStacks;
        _documents = documents;
        _views = views;
    }

    /// <inheritdoc />
    public ContextScope Scope => ContextScope.DiagramElement;

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<ContextActionGroupDefinition>> DiscoverAsync(ContextTarget target, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        cancellationToken.ThrowIfCancellationRequested();

        if (Resolve(target) is not { } found)
        {
            return Empty();
        }

        var (model, location) = found;
        if (!location.Target.IsEditable)
        {
            // Its text is somewhere else, so there is nothing here to do to it.
            return Empty();
        }

        var edits = new List<ContextActionDefinition>
        {
            new(RenameActionId, "Rename…", "mdi-pencil-outline", new ContextShortcutDefinition("F2")),
            new(
                ToggleEnabledActionId,
                Execution(location).IsDisabled ? "Enable" : "Disable",
                Execution(location).IsDisabled ? "mdi-play-circle-outline" : "mdi-pause-circle-outline"),
        };

        // Opening a stage is how its jobs are ever seen, so it goes first among what can be done
        // to one - and only where there is something inside to show (Requirement 8.2).
        if (location.Kind == PipelineElementLocationKind.Stage && location.Stage.Jobs.Count > 0)
        {
            var open = _views.For(target.WatchId, target.ResolvedFullPath).IsExpanded(location.Stage.Id);
            edits.Insert(0, new ContextActionDefinition(
                ToggleStageActionId,
                open ? "Hide jobs" : "Show jobs",
                open ? "mdi-unfold-less-horizontal" : "mdi-unfold-more-horizontal",
                new ContextShortcutDefinition(" ")));
        }

        if (location.Kind != PipelineElementLocationKind.Step && DependsOnDeclared(location))
        {
            // Only worth offering when there is something to clear: a stage with no dependsOn is
            // already on the default.
            edits.Add(new ContextActionDefinition(
                ClearDependenciesActionId,
                "Use the default order",
                "mdi-arrow-decision-outline"));
        }

        if (Removable(model, location))
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
        return Groups(
            new ContextActionGroupDefinition(edits),
            additions.Count > 0 ? new ContextActionGroupDefinition(additions) : null,
            ordering.Count > 0 ? new ContextActionGroupDefinition(ordering) : null);
    }

    /// <inheritdoc />
    public ValueTask<ContextExecutionResult> ExecuteAsync(ContextTarget target, string actionId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        cancellationToken.ThrowIfCancellationRequested();

        if (Resolve(target) is not { } found)
        {
            return Result(new ContextExecutionFailed(Gone));
        }

        var (_, location) = found;

        if (actionId == ToggleStageActionId)
        {
            // View state, not a command: nothing is written and nothing lands on the history,
            // because which stages this connection has open is not a fact about the pipeline.
            // Toggled through the view state's announcing method, which is what makes the session
            // push the deltas - toggling a view directly would change state no client hears of.
            _views.Toggle(target.WatchId, target.ResolvedFullPath, location.Stage.Id);
            return Result(new ContextExecutionCompleted());
        }

        // A rename asks for the new text first; everything else has all it needs already, so it
        // is committed straight away rather than putting a dialog in the way of one keystroke.
        return actionId == RenameActionId
            ? Result(new ContextExecutionRequiresInput(new ContextInputRequest(
                "Rename",
                "mdi-pencil-outline",
                "Display name",
                DisplayName(location),
                "Rename",
                target.ElementId)))
            : Result(new ContextExecutionCompleted());
    }

    /// <inheritdoc />
    /// <remarks>
    /// An empty display name is accepted, and means "remove it". An element without one falls back
    /// to its own identifying value - a stage to its name, a script step to its first line - so
    /// clearing it is a real instruction rather than a mistake to refuse.
    /// </remarks>
    public ValueTask<ContextValidationResult> ValidateAsync(ContextTarget target, string actionId, string value, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ = target;
        _ = actionId;
        _ = value;
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

        if (Resolve(target) is not { } found)
        {
            return ContextCommitResult.Failed(Gone);
        }

        var (_, location) = found;
        var command = CommandFor(target, actionId, value, location);
        if (command is null)
        {
            return ContextCommitResult.Failed($"'{actionId}' does not apply to this selection.");
        }

        // Through the project's history, so every one of these is one undo away like every other
        // edit in the IDE (Requirement 9.8).
        var result = await _historyStacks.Get(target.RootPath).ExecuteAsync(command, cancellationToken);
        return result.IsSuccess ? ContextCommitResult.Succeeded : ContextCommitResult.Failed(result.Error);
    }

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
            new(AddStageActionId, "Add stage", "mdi-layers-plus"),
            new(AddJobActionId, "Add job", "mdi-plus-box-outline"),
            new(AddDeploymentJobActionId, "Add deployment job", "mdi-rocket-launch-outline"),
        ],
        PipelineElementLocationKind.Job => [new(AddStepActionId, "Add step", "mdi-plus-box-outline")],
        _ => [],
    };

    /// <summary>
    /// Moving a step within its job - the one thing in a pipeline whose order is its meaning.
    /// </summary>
    /// <remarks>
    /// Withheld at the ends: a first step has nowhere to move up to. Withheld entirely where any
    /// of the job's steps came from a template, because then the order on the canvas is not the
    /// order in this file and moving by position would move the wrong lines.
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
                MoveStepUpActionId, "Move up", "mdi-arrow-up", new ContextShortcutDefinition("Alt+Up")));
        }

        if (index < location.Job.Steps.Count - 1)
        {
            actions.Add(new ContextActionDefinition(
                MoveStepDownActionId, "Move down", "mdi-arrow-down", new ContextShortcutDefinition("Alt+Down")));
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

    /// <summary>The model and the element a target names, or null when it names neither.</summary>
    private (PipelineModel Model, PipelineElementLocation Location)? Resolve(ContextTarget target)
    {
        if (target.Scope != ContextScope.DiagramElement || target.ElementId.Length == 0)
        {
            return null;
        }

        var entry = _documents.GetOrLoad(target.RootPath, target.ResolvedFullPath);
        if (!entry.IsUsable)
        {
            // Nothing may be edited in a file that does not parse, so nothing is offered on one.
            return null;
        }

        var location = PipelineEdits.Locate(entry.Model, target.ElementId);
        return location is null ? null : (entry.Model, location);
    }

    private static ValueTask<IReadOnlyList<ContextActionGroupDefinition>> Empty() =>
        ValueTask.FromResult<IReadOnlyList<ContextActionGroupDefinition>>([]);

    private static ValueTask<IReadOnlyList<ContextActionGroupDefinition>> Groups(params ContextActionGroupDefinition?[] groups) =>
        ValueTask.FromResult<IReadOnlyList<ContextActionGroupDefinition>>(
            groups.Where(group => group is not null).Select(group => group!).ToArray());

    private static ValueTask<ContextExecutionResult> Result(ContextExecutionResult result) =>
        ValueTask.FromResult(result);
}
