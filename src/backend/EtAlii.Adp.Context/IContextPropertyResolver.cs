namespace EtAlii.Adp.Context;

/// <summary>
/// Finds the properties of a target across every provider registered for its scope, and applies
/// a change to one. The service layer talks to this rather than to providers directly.
/// </summary>
public interface IContextPropertyResolver
{
    /// <summary>Every property every provider for this scope offers, in registration order.</summary>
    ValueTask<IReadOnlyList<ContextPropertyDefinition>> DescribeAsync(ContextTarget target, CancellationToken cancellationToken);

    /// <summary>
    /// Applies a value to the property with this id, through the provider that describes it.
    /// Refuses a property nobody offers, and one whose owner said it was read-only.
    /// </summary>
    ValueTask<ContextPropertyResult> SetAsync(ContextTarget target, string propertyId, string value, CancellationToken cancellationToken);
}
