using EtAlii.Adp.Common.Wire;
using EtAlii.Adp.Context;
using EtAlii.Adp.Hierarchy;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Problems;

/// <summary>
/// Offers <b>Validate</b> on a diagram and <b>Validate folder</b> on a folder - the label
/// says what it will cover (Requirement 7.2) - reaching the ribbon, the menu and the
/// keyboard (F6, Requirement 8.1) through the one path every action uses. A target that is
/// neither gets no entry at all, not a greyed one (Requirement 6.6).
/// </summary>
public sealed class ValidateContextActionProvider : IContextActionProvider
{
    public const string ValidateActionId = "problems.validate";

    private static readonly ContextShortcutDefinition ValidateShortcut = new("F6");

    private readonly IProblemStore _store;
    private readonly ProjectValidator _validator;
    private readonly DiagramFileRouter _router;

    public ValidateContextActionProvider(IProblemStore store, ProjectValidator validator, DiagramFileRouter router)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(validator);
        ArgumentNullException.ThrowIfNull(router);
        _store = store;
        _validator = validator;
        _router = router;
    }

    public ContextScope Scope => ContextScope.Hierarchy;

    public ValueTask<IReadOnlyList<ContextActionGroupDefinition>> DiscoverAsync(ContextTarget target, CancellationToken cancellationToken)
    {
        string label;
        if (target.IsContainer && HierarchyTargets.Exists(target))
        {
            label = "Validate folder";
        }
        else if (!target.IsContainer && File.Exists(target.ResolvedFullPath) && _router.Route(target.ResolvedFullPath) is DiagramRouted)
        {
            label = "Validate";
        }
        else
        {
            // Not ours to judge: a .txt file's menu gains no greyed entry (Requirement 6.6).
            return ValueTask.FromResult<IReadOnlyList<ContextActionGroupDefinition>>([]);
        }

        var group = new ContextActionGroupDefinition([
            new ContextActionDefinition(ValidateActionId, label, "mdi-check-circle-outline", ValidateShortcut),
        ]);
        return ValueTask.FromResult<IReadOnlyList<ContextActionGroupDefinition>>([group]);
    }

    public async ValueTask<ContextExecutionResult> ExecuteAsync(ContextTarget target, string actionId, CancellationToken cancellationToken)
    {
        if (actionId != ValidateActionId)
        {
            return new ContextExecutionFailed($"Unknown action '{actionId}'.");
        }
        if (!HierarchyTargets.Exists(target))
        {
            return new ContextExecutionFailed("This item is no longer there.");
        }

        var rootPath = target.RootPath;
        _store.BeginValidating(rootPath);

        if (target.IsContainer)
        {
            if (HierarchyTargets.IsRoot(target))
            {
                // Validating the root folder is validating the whole project.
                var everything = await _validator.ValidateAsync(new ProjectValidationScope(rootPath), cancellationToken);
                _store.Replace(rootPath, everything.Problems);
                return new ContextExecutionCompleted();
            }

            var relativeFolder = IoPath.GetRelativePath(rootPath, target.ResolvedFullPath);
            var outcome = await _validator.ValidateAsync(new FolderValidationScope(rootPath, relativeFolder), cancellationToken);
            _store.ReplaceFor(rootPath, [relativeFolder], outcome.Problems);
            return new ContextExecutionCompleted();
        }

        var relative = IoPath.GetRelativePath(rootPath, target.ResolvedFullPath);

        // What this validation speaks for: the file and the pair it belongs to, since a
        // body's problems live on its registration file. From the routing, not from what
        // was found - a pair that just became clean must still clear its old entries.
        var covered = new List<string> { relative };
        // Routed against the root, so a registration naming a shared body resolves to it.
        if (_router.Route(target.ResolvedFullPath, rootPath) is DiagramRouted routed)
        {
            if (routed.RegistrationPath is not null)
            {
                covered.Add(IoPath.GetRelativePath(rootPath, routed.RegistrationPath));
            }
            if (routed.BodyPath is { } bodyPath)
            {
                covered.Add(IoPath.GetRelativePath(rootPath, bodyPath));
            }
        }

        var fileOutcome = await _validator.ValidateAsync(new FileValidationScope(rootPath, relative), cancellationToken);
        covered.AddRange(fileOutcome.Problems.Select(problem => problem.RelativePath));
        _store.ReplaceFor(rootPath, covered.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), fileOutcome.Problems);
        return new ContextExecutionCompleted();
    }

    // Validation never prompts, so these two are unreachable through the interaction flow;
    // reaching them is a programming error, not something to answer to a user.
    public ValueTask<ContextValidationResult> ValidateAsync(ContextTarget target, string actionId, string value, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Validate takes no input.");

    public ValueTask<ContextCommitResult> CommitAsync(ContextTarget target, string actionId, string value, string text, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Validate does not commit an interaction.");
}
