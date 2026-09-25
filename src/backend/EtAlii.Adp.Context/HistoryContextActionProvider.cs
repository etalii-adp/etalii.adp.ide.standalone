using EtAlii.Adp.Documents.Wire;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Context;

/// <summary>
/// Offers undo and redo as ordinary context actions for the project scope, so they reach the
/// ribbon, the menu and the keyboard through the one path every action uses and need no RPC of
/// their own (diagram-undo-redo Requirement 5). Both are always offered; only their
/// availability changes, which is what a disabled undo button reads.
/// </summary>
public sealed class HistoryContextActionProvider : IContextActionProvider
{
    public const string UndoActionId = "history.undo";
    public const string RedoActionId = "history.redo";

    private static readonly ContextShortcutDefinition UndoShortcut = new("Z", Ctrl: true);
    private static readonly ContextShortcutDefinition RedoShortcut = new("Y", Ctrl: true);

    private readonly IHistoryStackStore _historyStacks;

    public HistoryContextActionProvider(IHistoryStackStore historyStacks)
    {
        ArgumentNullException.ThrowIfNull(historyStacks);
        _historyStacks = historyStacks;
    }

    public ContextScope Scope => ContextScope.Project;

    public ValueTask<IReadOnlyList<ContextActionGroupDefinition>> DiscoverAsync(ContextTarget target, CancellationToken cancellationToken)
    {
        var availability = _historyStacks.Get(target.RootPath).Availability;

        var group = new ContextActionGroupDefinition([
            new ContextActionDefinition(
                UndoActionId, "Undo", "mdi-undo", UndoShortcut,
                availability.CanUndo, availability.CanUndo ? "" : "There is nothing to undo."),
            new ContextActionDefinition(
                RedoActionId, "Redo", "mdi-redo", RedoShortcut,
                availability.CanRedo, availability.CanRedo ? "" : "There is nothing to redo."),
        ]);

        return ValueTask.FromResult<IReadOnlyList<ContextActionGroupDefinition>>([group]);
    }

    public async ValueTask<ContextExecutionResult> ExecuteAsync(ContextTarget target, string actionId, CancellationToken cancellationToken)
    {
        var stack = _historyStacks.Get(target.RootPath);
        var result = actionId switch
        {
            UndoActionId => await stack.UndoAsync(cancellationToken),
            RedoActionId => await stack.RedoAsync(cancellationToken),
            _ => CommandResult.Failure($"Unknown action '{actionId}'."),
        };

        return result.IsSuccess ? new ContextExecutionCompleted() : new ContextExecutionFailed(result.Error);
    }

    // Neither action prompts, so these two are unreachable through the interaction flow;
    // reaching them is a programming error, not something to answer to a user.
    public ValueTask<ContextValidationResult> ValidateAsync(ContextTarget target, string actionId, string value, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Undo and redo take no input.");

    public ValueTask<ContextCommitResult> CommitAsync(ContextTarget target, string actionId, string value, string text, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Undo and redo do not commit an interaction.");
}
