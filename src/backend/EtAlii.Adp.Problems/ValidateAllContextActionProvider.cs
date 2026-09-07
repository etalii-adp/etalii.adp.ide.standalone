using EtAlii.Adp.Common;
using EtAlii.Adp.Common.Wire;

namespace EtAlii.Adp.Problems;

/// <summary>
/// Offers <b>Validate all</b> for the errors-and-warnings panel itself
/// (<see cref="ContextScope.ProblemsPanel"/>): always offered, even with no validators
/// registered - the core checks still run (Requirement 7.6) - and greyed with a reason
/// while a run is in flight (Requirement 6.5). Reaches the ribbon when the panel takes
/// focus, its right-click menu, and Ctrl+Shift+B (Requirement 8.2), all as pushed data.
/// </summary>
public sealed class ValidateAllContextActionProvider : IContextActionProvider
{
    public const string ValidateAllActionId = "problems.validate.all";

    private static readonly ContextShortcutDefinition ValidateAllShortcut = new("B", Ctrl: true, Shift: true);

    private readonly IProblemStore _store;
    private readonly ProjectValidator _validator;

    public ValidateAllContextActionProvider(IProblemStore store, ProjectValidator validator)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(validator);
        _store = store;
        _validator = validator;
    }

    public ContextScope Scope => ContextScope.ProblemsPanel;

    public ValueTask<IReadOnlyList<ContextActionGroupDefinition>> DiscoverAsync(ContextTarget target, CancellationToken cancellationToken)
    {
        var running = _validator.IsValidating(target.RootPath);
        var group = new ContextActionGroupDefinition([
            new ContextActionDefinition(
                ValidateAllActionId, "Validate all", "mdi-check-all", ValidateAllShortcut,
                !running, running ? "A validation is already running." : ""),
        ]);
        return ValueTask.FromResult<IReadOnlyList<ContextActionGroupDefinition>>([group]);
    }

    public async ValueTask<ContextExecutionResult> ExecuteAsync(ContextTarget target, string actionId, CancellationToken cancellationToken)
    {
        if (actionId != ValidateAllActionId)
        {
            return new ContextExecutionFailed($"Unknown action '{actionId}'.");
        }

        // A second request while one runs joins the running traversal inside the validator
        // (Requirement 6.5, design deviation 2) - both callers get the same fresh answer.
        _store.BeginValidating(target.RootPath);
        var outcome = await _validator.ValidateAsync(new ProjectValidationScope(target.RootPath), cancellationToken);
        _store.Replace(target.RootPath, outcome.Problems);
        return new ContextExecutionCompleted();
    }

    // Validate all never prompts, so these two are unreachable through the interaction flow;
    // reaching them is a programming error, not something to answer to a user.
    public ValueTask<ContextValidationResult> ValidateAsync(ContextTarget target, string actionId, string value, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Validate all takes no input.");

    public ValueTask<ContextCommitResult> CommitAsync(ContextTarget target, string actionId, string value, string text, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Validate all does not commit an interaction.");
}
