using EtAlii.Adp.Context;
using EtAlii.Adp.Documents;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.WardleyMap;

/// <summary>
/// What a user can do to a Wardley element, offered the way every other action is - so it
/// reaches the ribbon, the right-click menu and the keyboard through one path, and the client
/// holds no key-to-action table of its own (Requirements 11.4, 11.5).
/// </summary>
/// <remarks>
/// <para>
/// On the shared frame (code-structure FR-034), with every action one record of
/// <see cref="WardleyActions"/>: what is offered, what is asked, and the command each makes. Every
/// action dispatches one of the module's commands through the project's history, so the menu, the
/// canvas and the property grid are three routes to one implementation with one inverse
/// (Requirement 9.8). The provider never touches the document.
/// </para>
/// <para>
/// <b>It answers from the target, not the model alone</b>: an element's id is the store's identity
/// for it, kept beside each map, so what an element offers is read with its map's identities.
/// </para>
/// </remarks>
public sealed class WardleyContextActionProvider(IHistoryStackStore historyStacks, IWardleyDocumentStore documents)
    : DiagramActionProvider<WardleyDocument, WardleyMap>(historyStacks, documents)
{
    public const string AddComponentActionId = "wardley.add-component";
    public const string AddAnchorActionId = "wardley.add-anchor";
    public const string AddSubmapActionId = "wardley.add-submap";
    public const string AddMarketActionId = "wardley.add-market";
    public const string AddEcosystemActionId = "wardley.add-ecosystem";
    public const string AddNoteActionId = "wardley.add-note";
    public const string AddAnnotationActionId = "wardley.add-annotation";
    public const string RenameActionId = "wardley.rename";
    public const string RemoveActionId = "wardley.remove";
    public const string SetEvolveActionId = "wardley.set-evolve";
    public const string ClearEvolveActionId = "wardley.clear-evolve";
    public const string ToggleInertiaActionId = "wardley.toggle-inertia";
    public const string LinkActionId = "wardley.link";
    public const string FlowActionId = "wardley.flow";
    public const string UnlinkActionId = "wardley.unlink";
    public const string AddToPipelineActionId = "wardley.add-to-pipeline";
    public const string RemoveFromPipelineActionId = "wardley.remove-from-pipeline";

    /// <summary>The toggle for one of the five decorators - `wardley.toggle-buy` and its kin.</summary>
    public static string DecoratorActionIdFor(WardleyDecorator decorator) =>
        $"wardley.toggle-{decorator.ToString().ToLowerInvariant()}";

    private const string Gone = "That element is no longer on this map.";

    /// <inheritdoc />
    protected override bool IsBody(string path) => Diagram.IsBody(path);

    /// <summary>
    /// A map of this module's, and one that can be written: nothing here can be done to a map that
    /// cannot, and Requirement 11.6 says an action that would fail is withheld rather than offered
    /// and refused.
    /// </summary>
    protected override bool Serves(ContextTarget target) =>
        base.Serves(target) && WardleyEditability.Editable(target.ResolvedFullPath);

    /// <inheritdoc />
    protected override IReadOnlyList<ContextActionGroupDefinition> Discover(ContextTarget target, DocumentEntry<WardleyDocument, WardleyMap> entry)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(entry);

        // A map that could not be read offers nothing: every action on it would be refused.
        return entry.IsUsable ? WardleyActions.All.Offered(Subject(target, entry.Model)) : [];
    }

    /// <inheritdoc />
    protected override ValueTask<ContextExecutionResult> RunAsync(
        ContextTarget target,
        DocumentEntry<WardleyDocument, WardleyMap> entry,
        string actionId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(entry);

        var subject = Subject(target, entry.Model);
        return WardleyActions.All.Applying(actionId, subject) is { } action
            ? RunActionAsync(target, action, subject, cancellationToken)
            : ValueTask.FromResult<ContextExecutionResult>(new ContextExecutionFailed(
                subject.IsGone ? Gone : $"'{actionId}' does not apply to this selection."));
    }

    /// <inheritdoc />
    protected override ContextValidationResult Validate(ContextTarget target, string actionId, string value) =>
        WardleyActions.All.Named(actionId)?.Validate(value) ?? ContextValidationResult.Accepted;

    /// <inheritdoc />
    protected override ICommand? CommitCommand(ContextTarget target, WardleyMap model, string actionId, string value)
    {
        var subject = Subject(target, model);
        return WardleyActions.All.Applying(actionId, subject)?.Command(subject, value);
    }

    private WardleySubject Subject(ContextTarget target, WardleyMap model) =>
        WardleySubject.Of(target.ResolvedFullPath, model, documents, target.ElementId);
}
