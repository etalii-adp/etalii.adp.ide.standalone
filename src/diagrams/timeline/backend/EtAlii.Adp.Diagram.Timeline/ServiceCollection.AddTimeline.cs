using Microsoft.Extensions.DependencyInjection;

namespace EtAlii.Adp.Diagram.Timeline;

/// <summary>
/// Registers everything the timeline module contributes, in one place.
/// </summary>
/// <remarks>
/// Invoked through <see cref="Diagram.Timeline"/>'s <c>Build</c> hook when discovery finds this
/// module - the host names nothing here, and core compiles and tests with this assembly absent.
/// Grows one line per seam as the module's tasks land, so at any commit it registers exactly
/// what exists.
/// </remarks>
public static class ServiceCollectionAddTimelineExtension
{
    /// <summary>Adds the timeline diagram module's seams.</summary>
    public static IServiceCollection AddTimeline(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        return services;
    }
}
