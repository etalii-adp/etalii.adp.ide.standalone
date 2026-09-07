using EtAlii.Adp.Common;
using EtAlii.Adp.Common.Wire;
using EtAlii.Adp.Context;

namespace EtAlii.Adp.Backend.Tests;

internal sealed class ContextActionResolverStubProvider : IContextActionProvider
{
    private readonly string _actionId;
    private readonly ContextShortcutDefinition? _shortcut;
    private readonly bool _available;
    private readonly Common.DiagramOrigin? _answersFor;

    public ContextActionResolverStubProvider(
        ContextScope scope,
        string actionId,
        ContextShortcutDefinition? shortcut = null,
        bool available = true,
        Common.DiagramOrigin? answersFor = null)
    {
        _answersFor = answersFor;
        Scope = scope;
        _actionId = actionId;
        _shortcut = shortcut;
        _available = available;
    }

    public ContextScope Scope { get; }

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
