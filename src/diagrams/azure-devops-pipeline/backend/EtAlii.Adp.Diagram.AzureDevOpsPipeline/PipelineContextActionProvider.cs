using EtAlii.Adp.Context;
using EtAlii.Adp.Diagram.Modules;
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
    public const string ToggleStageActionId = "azure-pipeline.toggle-stage";

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
    /// <remarks>
    /// Derived from the bundled definition (<see cref="PipelineDefinition.Menus"/>), open as this
    /// connection has the element: opening a stage or a job leads what can be done to one, and only
    /// where there is something inside to show (Requirement 8.2).
    /// </remarks>
    protected override IReadOnlyList<ContextActionGroupDefinition> Discover(ContextTarget target, DocumentEntry<LineDocument, PipelineModel> entry)
    {
        ArgumentNullException.ThrowIfNull(target);
        return PipelineDefinition.Menus(entry, _views.For(target.WatchId, target.ResolvedFullPath), target.ElementId);
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

        if (PipelineDefinition.IsView(actionId))
        {
            // View state, not a command: nothing is written and nothing lands on the history,
            // because which stages this connection has open is not a fact about the pipeline. The
            // definition's view action says what it should be; it is toggled through the view
            // state's announcing method, which is what makes the session push the deltas.
            var (toggle, refused) = PipelineDefinition.View(entry, _views.For(target.WatchId, target.ResolvedFullPath), target.ElementId, actionId);
            if (refused is { } refusal)
            {
                return new ContextExecutionFailed(refusal);
            }

            if (toggle is { } toggled)
            {
                _views.Toggle(target.WatchId, target.ResolvedFullPath, toggled);
            }

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

    private static int IndexOf(PipelineElementLocation location) =>
        location.Job!.Steps.ToList().FindIndex(step => step.Id == location.Step!.Id);

    /// <summary>The element <paramref name="elementId"/> names in a document that was read, or null.</summary>
    private static PipelineElementLocation? Resolve(DocumentEntry<LineDocument, PipelineModel> entry, string elementId) =>
        entry.IsUsable && elementId.Length > 0 ? PipelineEdits.Locate(entry.Model, elementId) : null;
}
