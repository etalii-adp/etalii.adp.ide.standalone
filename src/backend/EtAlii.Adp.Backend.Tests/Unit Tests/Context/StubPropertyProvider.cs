using EtAlii.Adp.Backend.Context;

namespace EtAlii.Adp.Backend.Tests;

/// <summary>
/// A property provider that describes what it was given and records what it was asked to write.
/// </summary>
internal sealed class StubPropertyProvider(
    ContextScope scope,
    IReadOnlyList<ContextPropertyDefinition> properties,
    string refusal = "") : IContextPropertyProvider
{
    /// <summary>Every set that reached this provider, in order - so a refusal upstream is visible as silence.</summary>
    public List<(string PropertyId, string Value)> Writes { get; } = [];

    public ContextScope Scope => scope;

    public ValueTask<IReadOnlyList<ContextPropertyDefinition>> DescribeAsync(ContextTarget target, CancellationToken cancellationToken) =>
        ValueTask.FromResult(properties);

    public ValueTask<ContextPropertyResult> SetAsync(
        ContextTarget target,
        string propertyId,
        string value,
        CancellationToken cancellationToken)
    {
        Writes.Add((propertyId, value));
        return ValueTask.FromResult(
            refusal.Length > 0 ? ContextPropertyResult.Failure(refusal) : ContextPropertyResult.Success);
    }
}
