using EtAlii.Adp.Documents;
using EtAlii.Adp.Documents.Wire;

namespace EtAlii.Adp.Context.Tests;

internal sealed class ContextActionResolverStubProvider(
    ContextScope scope,
    string actionId,
    ContextShortcutDefinition? shortcut = null,
    bool available = true,
    DiagramOrigin? answersFor = null) : IContextActionProvider
{
    private readonly string _actionId = actionId;
    private readonly ContextShortcutDefinition? _shortcut = shortcut;
    private readonly bool _available = available;
    private readonly DiagramOrigin? _answersFor = answersFor;

    public ContextScope Scope { get; } = scope;

    public bool WasConsulted { get; private set; }

    public ValueTask<IReadOnlyList<ContextActionGroupDefinition>> DiscoverAsync(ContextTarget target, CancellationToken cancellationToken)
    {
        WasConsulted = true;

        // A reading's provider answers only for its own origin; one built without an origin
        // answers for anything, which is what a family provider - and every provider written
        // before the field existed - does.
        if (_answersFor is not null && target.Origin != _answersFor)
        {
            return ValueTask.FromResult<IReadOnlyList<ContextActionGroupDefinition>>([]);
        }
        var group = new ContextActionGroupDefinition(new[]
        {
            new ContextActionDefinition(_actionId, _actionId, "mdi-circle", _shortcut, _available),
        });
        return ValueTask.FromResult<IReadOnlyList<ContextActionGroupDefinition>>(new[] { group });
    }

    public ValueTask<ContextExecutionResult> ExecuteAsync(ContextTarget target, string actionId, CancellationToken cancellationToken) =>
        ValueTask.FromResult<ContextExecutionResult>(new ContextExecutionCompleted());

    public ValueTask<ContextValidationResult> ValidateAsync(ContextTarget target, string actionId, string value, CancellationToken cancellationToken) =>
        ValueTask.FromResult(ContextValidationResult.Accepted);

    public ValueTask<ContextCommitResult> CommitAsync(ContextTarget target, string actionId, string value, string text, CancellationToken cancellationToken) =>
        ValueTask.FromResult(ContextCommitResult.Succeeded);
}
