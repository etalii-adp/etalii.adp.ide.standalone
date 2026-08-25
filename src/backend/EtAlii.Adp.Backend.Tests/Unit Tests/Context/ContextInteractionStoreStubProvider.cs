using System.Threading.Channels;
using EtAlii.Adp.Backend.Context;
using Xunit;

namespace EtAlii.Adp.Backend.Tests;

internal sealed class ContextInteractionStoreStubProvider : IContextActionProvider
{
    public ContextScope Scope => ContextScope.Hierarchy;

    public ValueTask<IReadOnlyList<ContextActionGroupDefinition>> DiscoverAsync(ContextTarget target, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<ContextActionGroupDefinition>>(Array.Empty<ContextActionGroupDefinition>());

    public ValueTask<ContextExecutionResult> ExecuteAsync(ContextTarget target, string actionId, CancellationToken cancellationToken) =>
        ValueTask.FromResult<ContextExecutionResult>(new ContextExecutionCompleted());

    public ValueTask<ContextValidationResult> ValidateAsync(ContextTarget target, string actionId, string value, CancellationToken cancellationToken) =>
        ValueTask.FromResult(ContextValidationResult.Accepted);

    public ValueTask<ContextCommitResult> CommitAsync(ContextTarget target, string actionId, string value, string text, CancellationToken cancellationToken) =>
        ValueTask.FromResult(ContextCommitResult.Succeeded);
}
