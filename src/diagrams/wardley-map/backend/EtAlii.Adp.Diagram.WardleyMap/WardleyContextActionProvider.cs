using EtAlii.Adp.Context;
using EtAlii.Adp.Diagram.Modules;
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
/// <b>The menus, the dialogs, what they refuse and what each entry writes are the definition's</b>
/// (wardley-map-disl-fbl Requirement 3.1): derived by <see cref="WardleyDefinition"/> from the bundled
/// DISL definition, and written by today's commands (<see cref="WardleyTransactionCommands"/>) through
/// the project's history. This provider computes none of it.
/// </para>
/// <para>
/// <b>It answers from the target, not the model alone</b>: an element's id is the store's identity
/// for it, kept beside each map, so what an element offers is read with its map's identities.
/// </para>
/// <para>
/// A placement on empty map is answered as the map itself (<see cref="WardleySelection.SubjectOf"/>), so
/// a right-click there offers the definition's menu for the diagram.
/// </para>
/// </remarks>
public sealed class WardleyContextActionProvider(IHistoryStackStore historyStacks, IWardleyDocumentStore documents)
    : DiagramActionProvider<LineDocument, WardleyMap>(historyStacks, documents)
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
    protected override IReadOnlyList<ContextActionGroupDefinition> Discover(ContextTarget target, DocumentEntry<LineDocument, WardleyMap> entry)
    {
        ArgumentNullException.ThrowIfNull(target);
        return WardleyDefinition.Menus(entry, documents.Identities(target.ResolvedFullPath), WardleySelection.SubjectOf(target.ElementId), editable: true);
    }

    /// <inheritdoc />
    protected override async ValueTask<ContextExecutionResult> RunAsync(
        ContextTarget target,
        DocumentEntry<LineDocument, WardleyMap> entry,
        string actionId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);

        var step = WardleyDefinition.Execute(entry, documents.Identities(target.ResolvedFullPath), target.ResolvedFullPath, WardleySelection.SubjectOf(target.ElementId), actionId);
        return step switch
        {
            { Answer: { } answer } => answer,
            { Command: { } command } => await RemoveAsync(target, command, step.Confirmation, cancellationToken),
            _ => new ContextExecutionFailed($"'{actionId}' does not apply to this selection."),
        };
    }

    /// <inheritdoc />
    protected override ContextValidationResult Validate(ContextTarget target, DocumentEntry<LineDocument, WardleyMap> entry, string actionId, string value)
    {
        ArgumentNullException.ThrowIfNull(target);
        return WardleyDefinition.Validate(entry, documents.Identities(target.ResolvedFullPath), WardleySelection.SubjectOf(target.ElementId), actionId, value) is { } refusal
            ? ContextValidationResult.Rejected(refusal)
            : ContextValidationResult.Accepted;
    }

    /// <inheritdoc />
    protected override DiagramPropertyChange Commit(ContextTarget target, DocumentEntry<LineDocument, WardleyMap> entry, string actionId, string value)
    {
        ArgumentNullException.ThrowIfNull(target);
        return WardleyDefinition.Commit(entry, documents.Identities(target.ResolvedFullPath), target.ResolvedFullPath, WardleySelection.SubjectOf(target.ElementId), actionId, value);
    }
}
