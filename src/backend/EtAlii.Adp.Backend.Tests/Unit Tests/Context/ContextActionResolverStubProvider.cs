using EtAlii.Adp.Backend.Context;
using Xunit;

namespace EtAlii.Adp.Backend.Tests;

internal sealed class ContextActionResolverStubProvider : IContextActionProvider
{
    private readonly string _actionId;
    private readonly ContextShortcutDefinition? _shortcut;
    private readonly bool _available;

    public ContextActionResolverStubProvider(ContextScope scope, string actionId, ContextShortcutDefinition? shortcut = null, bool available = true)
    {
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
