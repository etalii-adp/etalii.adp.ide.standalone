using EtAlii.Adp.Backend.Context;
using EtAlii.Adp.Diagram;

namespace EtAlii.Adp.Backend.Hierarchy;

/// <summary>
/// Offers <b>Add…</b> on any folder - the project root included - and carries the chosen
/// diagram type to the point where a file would be created.
/// </summary>
/// <remarks>
/// This spec stops at that point: <see cref="CommitAsync"/> re-checks the folder, resolves the
/// choice, and answers that creating the type is not supported yet. The checks before that
/// answer are deliberately ordered and pinned by tests, because the spec that fills in
/// creation (create-diagram-file) replaces only the final answer and inherits them.
/// <para>
/// The definitions come in through the constructor with <see cref="DiagramDefinition.All"/>
/// as the default, so a test can hand the provider its own list instead of filling the
/// process-wide cache.
/// </para>
/// </remarks>
public sealed class AddDiagramContextActionProvider : IContextActionProvider
{
    public const string AddActionId = "hierarchy.add";
    private const string NoDiagramTypes = "No diagram types are available.";
    private static readonly ContextShortcutDefinition AddShortcut = new("Insert");

    private readonly Func<IReadOnlyList<DiagramDefinition>> _definitions;

    public AddDiagramContextActionProvider()
        : this(null)
    {
    }

    /// <param name="definitions">
    /// The diagram types to offer, read at call time; <c>null</c> means
    /// <see cref="DiagramDefinition.All"/>. Read lazily rather than captured, because the
    /// host fills the cache after the container is built.
    /// </param>
    public AddDiagramContextActionProvider(IReadOnlyList<DiagramDefinition>? definitions)
    {
        _definitions = definitions is null ? () => DiagramDefinition.All : () => definitions;
    }

    public ContextScope Scope => ContextScope.Hierarchy;

    public ValueTask<IReadOnlyList<ContextActionGroupDefinition>> DiscoverAsync(ContextTarget target, CancellationToken cancellationToken)
    {
        // A file cannot contain a new entry, and a folder that is gone cannot either.
        if (!target.IsContainer || !HierarchyTargets.Exists(target))
        {
            return ValueTask.FromResult<IReadOnlyList<ContextActionGroupDefinition>>([]);
        }

        var available = _definitions().Count > 0;
        var group = new ContextActionGroupDefinition([
            new ContextActionDefinition(
                AddActionId,
                "Add…",
                "mdi-plus",
                AddShortcut,
                available,
                available ? "" : NoDiagramTypes),
        ]);

        return ValueTask.FromResult<IReadOnlyList<ContextActionGroupDefinition>>([group]);
    }

    public ValueTask<ContextExecutionResult> ExecuteAsync(ContextTarget target, string actionId, CancellationToken cancellationToken)
    {
        if (actionId != AddActionId)
        {
            return ValueTask.FromResult<ContextExecutionResult>(new ContextExecutionResult.Failed($"Unknown action '{actionId}'."));
        }

        if (!target.IsContainer || !HierarchyTargets.Exists(target))
        {
            return ValueTask.FromResult<ContextExecutionResult>(new ContextExecutionResult.Failed("The folder no longer exists."));
        }

        return ValueTask.FromResult<ContextExecutionResult>(new ContextExecutionResult.RequiresChoice(
            new ContextChoiceRequest(
                Title: "Add diagram",
                Icon: "mdi-plus",
                ConfirmLabel: "Add",
                Options: DiagramOptionTree.Build(_definitions()),
                EmptyMessage: NoDiagramTypes)));
    }

    /// <summary>
    /// There is no free text to judge: a choice is either a known option id or it is not, and
    /// <see cref="CommitAsync"/> is where that is decided.
    /// </summary>
    public ValueTask<ContextValidationResult> ValidateAsync(ContextTarget target, string actionId, string value, CancellationToken cancellationToken)
        => ValueTask.FromResult(ContextValidationResult.Accepted);

    /// <summary>
    /// The seam. In this order: the folder is re-checked (it may have vanished while the
    /// dialog was open), the option id is resolved to a definition, and then - for now -
    /// the answer is that creating that type is not supported yet. A later spec replaces
    /// only that last answer.
    /// </summary>
    public ValueTask<ContextCommitResult> CommitAsync(ContextTarget target, string actionId, string value, CancellationToken cancellationToken)
    {
        if (actionId != AddActionId)
        {
            return ValueTask.FromResult(ContextCommitResult.Failed($"Unknown action '{actionId}'."));
        }

        if (!target.IsContainer || !HierarchyTargets.Exists(target))
        {
            return ValueTask.FromResult(ContextCommitResult.Failed("The folder no longer exists."));
        }

        var definition = _definitions().FirstOrDefault(candidate => candidate.Origin.Key == value);
        if (definition is null)
        {
            return ValueTask.FromResult(ContextCommitResult.Failed("That diagram type is not available."));
        }

        // Nothing is written to disk in this spec.
        return ValueTask.FromResult(ContextCommitResult.Failed($"Creating a {definition.Title} diagram is not supported yet."));
    }
}
