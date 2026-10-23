using EtAlii.Adp.Context;
using EtAlii.Adp.History;
using ContextScope = EtAlii.Adp.Documents.Wire.ContextScope;

namespace EtAlii.Adp.Designer.HierarchicalRequirement;

/// <summary>
/// The keys that add an entry directly below the selected one, with no dialog (FR-012, US4 scenario 3): Ctrl+Enter adds
/// an item below an item and a requirement below a requirement, Ctrl+Shift+Enter a requirement to an item. They are
/// offered the way every other action is, so the menu and the ribbon show them too, each with its shortcut.
/// </summary>
/// <remarks>
/// <para>
/// <b>The edit is the page's own.</b> An action makes the same command a gesture on the page makes, against the bytes
/// of the file as they are now, through the project's history: one undoable step, and an open page shows it at once.
/// The page opens the new entry's title for typing, as it does for an entry its own control added.
/// </para>
/// <para>
/// <b>Only a requirement document answers.</b> Every element of every tool is asked; a file without this designer's
/// envelope is passed over without being read further.
/// </para>
/// </remarks>
internal sealed class HierarchicalRequirementContextActionProvider(IHistoryStackStore histories) : IContextActionProvider
{
    public const string AddBelowActionId = "hierarchical-requirement.add-below";
    public const string AddRequirementActionId = "hierarchical-requirement.add-requirement";

    /// <summary>Ctrl+Enter: a new entry of the selected one's kind, directly below it.</summary>
    public static ContextShortcutDefinition BelowShortcut { get; } = new("Enter", Ctrl: true);

    /// <summary>Ctrl+Shift+Enter: a new requirement in the selected item.</summary>
    public static ContextShortcutDefinition RequirementShortcut { get; } = new("Enter", Ctrl: true, Shift: true);

    private const string EntryGone = "That entry is no longer in the file.";

    public ContextScope Scope => ContextScope.DiagramElement;

    public ValueTask<IReadOnlyList<ContextActionGroupDefinition>> DiscoverAsync(ContextTarget target, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        cancellationToken.ThrowIfCancellationRequested();
        if (Resolve(target) is not { Entry: var entry })
        {
            return ValueTask.FromResult<IReadOnlyList<ContextActionGroupDefinition>>([]);
        }

        // The labels and the unavailable message are the specification's (hierarchical-requirement.des, operations).
        IReadOnlyList<ContextActionDefinition> actions = entry is Item
            ?
            [
                new ContextActionDefinition(AddBelowActionId, "New item below", "mdi-plus-box-outline", BelowShortcut),
                new ContextActionDefinition(AddRequirementActionId, "New requirement", "mdi-plus", RequirementShortcut),
            ]
            :
            [
                new ContextActionDefinition(AddBelowActionId, "New requirement below", "mdi-plus", BelowShortcut, entry.ParentId is not null, entry.ParentId is null ? StructureOperations.NoItem : ""),
            ];
        return ValueTask.FromResult<IReadOnlyList<ContextActionGroupDefinition>>([new ContextActionGroupDefinition(actions)]);
    }

    public async ValueTask<ContextExecutionResult> ExecuteAsync(ContextTarget target, string actionId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (Resolve(target) is not { } resolved)
        {
            return new ContextExecutionFailed(EntryGone);
        }

        var operation = (actionId, resolved.Entry) switch
        {
            (AddBelowActionId, Item) => StructureOperations.AddItemBelow,
            (AddBelowActionId, Requirement) => StructureOperations.AddRequirementBelow,
            (AddRequirementActionId, Item) => StructureOperations.AddRequirement,
            _ => null,
        };
        if (operation is null)
        {
            return new ContextExecutionFailed($"Unknown action '{actionId}'.");
        }

        var command = new HierarchicalRequirementEditCommand(target.ResolvedFullPath, operation, target.ElementId, new Dictionary<string, string>(StringComparer.Ordinal), resolved.Bytes);
        var result = await histories.Get(target.Root).ExecuteAsync(command, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? new ContextExecutionCompleted() : new ContextExecutionFailed(result.Error);
    }

    public ValueTask<ContextValidationResult> ValidateAsync(ContextTarget target, string actionId, string value, CancellationToken cancellationToken) =>
        ValueTask.FromResult(ContextValidationResult.Accepted);

    // Neither action asks anything, so there is never a value to commit.
    public ValueTask<ContextCommitResult> CommitAsync(ContextTarget target, string actionId, string value, string text, CancellationToken cancellationToken) =>
        ValueTask.FromResult(ContextCommitResult.Failed($"'{actionId}' asks nothing to commit."));

    /// <summary>The entry the target names in the requirement document it is in, with the bytes it was read from; null for anything else, and for a file that cannot be changed.</summary>
    private static HierarchicalRequirementEntries.Found? Resolve(ContextTarget target) =>
        target.Scope == ContextScope.DiagramElement && HierarchicalRequirementEntries.Find(target.ResolvedFullPath, target.ElementId) is { Document.ReadOnlyReason.Length: 0 } found ? found : null;
}
