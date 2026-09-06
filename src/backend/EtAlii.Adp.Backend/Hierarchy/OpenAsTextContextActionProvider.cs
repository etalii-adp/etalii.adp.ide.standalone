using EtAlii.Adp.Backend.Context;
using EtAlii.Adp.Common;
using EtAlii.Adp.Common.Wire;
using EtAlii.Adp.Editor;
using Serilog;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Backend.Hierarchy;

/// <summary>
/// Offers <b>Open as text</b> on a file the diagram family claims - the way to reach the text
/// underneath a diagram when the canvas cannot express what is needed (modular-text-editors
/// Requirement 5.2) - and <b>Open with…</b> when two editors legitimately share the file's
/// extension with a declared default (Requirement 4.4).
/// </summary>
/// <remarks>
/// Mirrors <see cref="AddDiagramContextActionProvider"/>'s shape: resolved by
/// <see cref="ContextScope"/>, routing through <see cref="DiagramFileRouter"/> and
/// <see cref="EditorResolver"/> - core types both, never a specific module. The execute is
/// deliberately thin: the workspace's tabs are per-connection client state (like the
/// selection-driven tab open), so completing the action tells the client its request stands
/// and the client opens the tab, whose stream then forces the editor family through
/// <c>OpenDiagramRequest.editor_id</c>. Nothing here synchronises the two views of one file:
/// each session independently reads and writes the same path, so the file on disk is the
/// tie-breaker by construction (Requirement 5.5) - adding synchronisation code would be
/// solving a problem that does not exist.
/// </remarks>
public sealed class OpenAsTextContextActionProvider : IContextActionProvider
{
    public const string OpenAsTextActionId = "editor.open-as-text";
    public const string OpenWithActionId = "editor.open-with";
    private static readonly ILogger _logger = Log.ForContext<OpenAsTextContextActionProvider>();

    private readonly DiagramFileRouter _router;
    private readonly EditorResolver _editorResolver;

    public OpenAsTextContextActionProvider(DiagramFileRouter router, EditorResolver editorResolver)
    {
        ArgumentNullException.ThrowIfNull(router);
        ArgumentNullException.ThrowIfNull(editorResolver);
        _router = router;
        _editorResolver = editorResolver;
    }

    public ContextScope Scope => ContextScope.Hierarchy;

    public ValueTask<IReadOnlyList<ContextActionGroupDefinition>> DiscoverAsync(ContextTarget target, CancellationToken cancellationToken)
    {
        // Folders have no text underneath, and a file that is not a diagram already opens as
        // text on activation (Requirement 5.4) - offering "Open as text" there would promise
        // something double-click does not already deliver only on the files where it is
        // meaningless. The action belongs exactly where the diagram claimed the double-click.
        if (target.IsContainer || !HierarchyTargets.Exists(target)
            || _router.Route(target.ResolvedFullPath, target.RootPath) is not DiagramRouted)
        {
            return ValueTask.FromResult<IReadOnlyList<ContextActionGroupDefinition>>([]);
        }

        var routing = _editorResolver.Resolve(target.ResolvedFullPath);
        var actions = new List<ContextActionDefinition>
        {
            new(
                OpenAsTextActionId,
                "Open as text",
                "mdi-file-document-outline",
                Shortcut: null,
                Available: routing is EditorRouted,
                // The startup log already named the rivals; the menu names the consequence.
                UnavailableReason: routing is EditorRouted ? "" : "Rival editors claim this file and none is the default."),
        };

        // "Open with…" appears only for the legitimately shared case: several claimants, and
        // the resolver still routing because one declared itself the default (Requirement
        // 4.4). An undefaulted conflict is a deployment error that stays one, and a solely
        // claimed or fallback-only file has nothing to choose between.
        if (routing is EditorRouted && ClaimantsFor(target).Count > 1)
        {
            actions.Add(new ContextActionDefinition(
                OpenWithActionId,
                "Open with…",
                "mdi-open-in-app"));
        }

        return ValueTask.FromResult<IReadOnlyList<ContextActionGroupDefinition>>([new ContextActionGroupDefinition(actions)]);
    }

    public ValueTask<ContextExecutionResult> ExecuteAsync(ContextTarget target, string actionId, CancellationToken cancellationToken)
    {
        if (actionId is not (OpenAsTextActionId or OpenWithActionId))
        {
            return ValueTask.FromResult<ContextExecutionResult>(new ContextExecutionFailed($"Unknown action '{actionId}'."));
        }

        if (!HierarchyTargets.Exists(target))
        {
            return ValueTask.FromResult<ContextExecutionResult>(new ContextExecutionFailed("The file no longer exists."));
        }

        if (actionId == OpenAsTextActionId)
        {
            if (_editorResolver.Resolve(target.ResolvedFullPath) is not EditorRouted)
            {
                return ValueTask.FromResult<ContextExecutionResult>(
                    new ContextExecutionFailed("Rival editors claim this file and none is the default."));
            }

            // Completed is the whole backend half: the tab, like every workspace tab, is
            // per-connection client state, and the stream the client then opens carries the
            // forced editor resolution.
            return ValueTask.FromResult<ContextExecutionResult>(new ContextExecutionCompleted());
        }

        var claimants = ClaimantsFor(target);
        if (claimants.Count < 2)
        {
            return ValueTask.FromResult<ContextExecutionResult>(new ContextExecutionFailed("Only one editor reads this kind of file."));
        }

        return ValueTask.FromResult<ContextExecutionResult>(new ContextExecutionRequiresChoice(
            new ContextChoiceRequest(
                Title: $"Open {IoPath.GetFileName(target.ResolvedFullPath)} with",
                Icon: "mdi-open-in-app",
                ConfirmLabel: "Open",
                Options: [.. claimants.Select(claimant => new ContextOptionNode(
                    Id: claimant.Id,
                    Label: claimant.Title,
                    Selectable: true,
                    Description: claimant.Description,
                    Icon: claimant.Icon))],
                EmptyMessage: "No editors are available.",
                NameField: null)));
    }

    /// <summary>Nothing to judge: the dialog has no name field, and the chosen option is judged by <see cref="CommitAsync"/>.</summary>
    public ValueTask<ContextValidationResult> ValidateAsync(ContextTarget target, string actionId, string value, CancellationToken cancellationToken)
        => ValueTask.FromResult(ContextValidationResult.Accepted);

    public ValueTask<ContextCommitResult> CommitAsync(ContextTarget target, string actionId, string value, string text, CancellationToken cancellationToken)
    {
        if (actionId != OpenWithActionId)
        {
            return ValueTask.FromResult(ContextCommitResult.Failed($"Unknown action '{actionId}'."));
        }

        if (!HierarchyTargets.Exists(target))
        {
            return ValueTask.FromResult(ContextCommitResult.Failed("The file no longer exists."));
        }

        // Checked against the claimants rather than every editor, so a stale dialog - or a
        // client built against a different set of modules - cannot open the file with an
        // editor that never claimed it.
        if (ClaimantsFor(target).All(claimant => claimant.Id != value))
        {
            _logger.Warning("Rejecting the Open with: {OptionId} does not claim {File}", value, target.ResolvedFullPath);
            return ValueTask.FromResult(ContextCommitResult.Failed("That editor is not available for this file."));
        }

        // Nothing was created and nothing more happens here: the client opens the tab, naming
        // the chosen editor on the stream it opens.
        return ValueTask.FromResult(ContextCommitResult.Succeeded);
    }

    private IReadOnlyList<EditorDefinition> ClaimantsFor(ContextTarget target) =>
        _editorResolver.ClaimantsOf(target.ResolvedFullPath);
}
