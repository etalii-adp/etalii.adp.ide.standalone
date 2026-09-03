using EtAlii.Adp.Backend.Hierarchy;

namespace EtAlii.Adp.Diagram.Databricks;

/// <summary>
/// The bundle diagram's computed layout: three bands - the bundle itself, the resources it
/// declares, and the target frames beneath (databricks-diagrams Requirement 3). A pure
/// function; the layout overlay merges authored positions on top.
/// </summary>
internal static class DatabricksBundleLayout
{
    private const double ColumnWidth = 260;
    private const double BundleBandY = 0;
    private const double ResourceBandY = 180;
    private const double TargetBandY = 400;

    /// <summary>
    /// A position for the bundle node, every resource (unknown kinds included - they draw
    /// generically rather than vanish), and every target frame. Ids as the mapper speaks them:
    /// <c>bundle</c>, <c>resource:</c>, <c>unknown:</c>, <c>target:</c>.
    /// </summary>
    public static IReadOnlyDictionary<string, RegistrationPosition> Positions(BundleModel bundle)
    {
        ArgumentNullException.ThrowIfNull(bundle);

        var positions = new Dictionary<string, RegistrationPosition>(StringComparer.Ordinal)
        {
            ["bundle"] = new(0, BundleBandY),
        };

        var column = 0;
        foreach (var resource in bundle.Resources)
        {
            positions[$"resource:{resource.Kind}/{resource.Key}"] =
                new RegistrationPosition(column * ColumnWidth, ResourceBandY);
            column++;
        }

        foreach (var unknown in bundle.UnknownNodes)
        {
            positions[$"unknown:{unknown.Path}"] =
                new RegistrationPosition(column * ColumnWidth, ResourceBandY);
            column++;
        }

        for (var index = 0; index < bundle.Targets.Count; index++)
        {
            positions[$"target:{bundle.Targets[index].Name}"] =
                new RegistrationPosition(index * ColumnWidth, TargetBandY);
        }

        return positions;
    }
}
