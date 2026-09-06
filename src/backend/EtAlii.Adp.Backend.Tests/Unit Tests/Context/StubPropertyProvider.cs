using EtAlii.Adp.Backend.Context;
using EtAlii.Adp.Common;
using EtAlii.Adp.Common.Wire;

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

    /// <summary>A module that is broken rather than merely empty: describing throws.</summary>
    public bool ThrowOnDescribe { get; init; }

    /// <summary>A module whose write itself fails - which must never be reported as accepted.</summary>
    public bool ThrowOnSet { get; init; }

    public ContextScope Scope => scope;

    public ValueTask<IReadOnlyList<ContextPropertyDefinition>> DescribeAsync(ContextTarget target, CancellationToken cancellationToken) =>
        ThrowOnDescribe
            ? throw new InvalidOperationException("This provider is broken.")
            : ValueTask.FromResult(properties);

    public ValueTask<ContextPropertyResult> SetAsync(
        ContextTarget target,
        string propertyId,
        string value,
        CancellationToken cancellationToken)
    {
        Writes.Add((propertyId, value));
        return ThrowOnSet
            ? throw new InvalidOperationException("This provider could not write.")
            : ValueTask.FromResult(refusal.Length > 0 ? ContextPropertyResult.Failure(refusal) : ContextPropertyResult.Success);
    }
}
