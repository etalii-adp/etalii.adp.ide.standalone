using EtAlii.Adp.Context;
using EtAlii.Adp.Documents.Wire;

namespace EtAlii.Adp.Hierarchy;

/// <summary>
/// Offers <b>New…</b> on the project itself, so the ribbon has a way to start a tool that needs no
/// folder selected first: it asks what <see cref="AddDiagramContextActionProvider"/>'s <b>Add…</b>
/// asks on the project root, and creates there. A new user pressed the ribbon's old <i>New</i>,
/// which did nothing (etalii.adp spec 092, host test T035).
/// </summary>
public sealed class NewToolContextActionProvider : IContextActionProvider
{
    public const string NewActionId = "project.new";

    private readonly AddDiagramContextActionProvider _add;

    public NewToolContextActionProvider(AddDiagramContextActionProvider add)
    {
        ArgumentNullException.ThrowIfNull(add);
        _add = add;
    }

    public ContextScope Scope => ContextScope.Project;

    public async ValueTask<IReadOnlyList<ContextActionGroupDefinition>> DiscoverAsync(ContextTarget target, CancellationToken cancellationToken)
    {
        // Available exactly when Add… is on the project root, and unavailable for the same reason.
        var offered = await _add.DiscoverAsync(RootOf(target), cancellationToken);
        var add = offered.SelectMany(group => group.Actions).FirstOrDefault(action => action.Id == AddDiagramContextActionProvider.AddActionId);
        if (add is null)
        {
            return [];
        }

        return [new ContextActionGroupDefinition([new ContextActionDefinition(NewActionId, "New…", "mdi-file-plus-outline", null, add.Available, add.UnavailableReason)])];
    }

    public ValueTask<ContextExecutionResult> ExecuteAsync(ContextTarget target, string actionId, CancellationToken cancellationToken) =>
        actionId == NewActionId
            ? _add.ExecuteAsync(RootOf(target), AddDiagramContextActionProvider.AddActionId, cancellationToken)
            : ValueTask.FromResult<ContextExecutionResult>(new ContextExecutionFailed($"Unknown action '{actionId}'."));

    public ValueTask<ContextValidationResult> ValidateAsync(ContextTarget target, string actionId, string value, CancellationToken cancellationToken) =>
        _add.ValidateAsync(RootOf(target), AddDiagramContextActionProvider.AddActionId, value, cancellationToken);

    public ValueTask<ContextCommitResult> CommitAsync(ContextTarget target, string actionId, string value, string text, CancellationToken cancellationToken) =>
        actionId == NewActionId
            ? _add.CommitAsync(RootOf(target), AddDiagramContextActionProvider.AddActionId, value, text, cancellationToken)
            : ValueTask.FromResult(ContextCommitResult.Failed($"Unknown action '{actionId}'."));

    /// <summary>The project as the hierarchy sees it: its root folder, which carries no entry id.</summary>
    private static ContextTarget RootOf(ContextTarget project) =>
        project with { Scope = ContextScope.Hierarchy, IsContainer = true, SourceId = default };
}
