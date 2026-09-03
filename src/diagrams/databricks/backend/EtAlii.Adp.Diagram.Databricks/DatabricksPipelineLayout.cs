using EtAlii.Adp.Backend.Hierarchy;

namespace EtAlii.Adp.Diagram.Databricks;

/// <summary>
/// The pipeline diagram's computed layout: source libraries flow left-to-right into the
/// pipeline and on to its catalog target, with the satellites - compute, notifications -
/// beneath (databricks-diagrams Requirement 5). A pure function; the layout overlay merges
/// authored positions on top.
/// </summary>
internal static class DatabricksPipelineLayout
{
    private const double SourceColumnX = 0;
    private const double PipelineColumnX = 320;
    private const double TargetColumnX = 640;
    private const double RowHeight = 120;
    private const double SatelliteBandGap = 80;

    /// <summary>
    /// A position for every library, the pipeline node, its target, and the satellites it
    /// declares. Ids as the mapper speaks them: <c>library:</c>, <c>pipeline</c>, <c>target</c>,
    /// <c>compute</c>, <c>notifications</c>.
    /// </summary>
    public static IReadOnlyDictionary<string, RegistrationPosition> Positions(PipelineModel pipeline)
    {
        ArgumentNullException.ThrowIfNull(pipeline);

        var positions = new Dictionary<string, RegistrationPosition>(StringComparer.Ordinal);

        for (var index = 0; index < pipeline.Libraries.Count; index++)
        {
            positions[$"library:{pipeline.Libraries[index].Path}"] =
                new RegistrationPosition(SourceColumnX, index * RowHeight);
        }

        // The pipeline sits at its sources' vertical middle, the target across from it.
        var middle = Math.Max(pipeline.Libraries.Count - 1, 0) * RowHeight / 2;
        positions["pipeline"] = new RegistrationPosition(PipelineColumnX, middle);
        positions["target"] = new RegistrationPosition(TargetColumnX, middle);

        var satelliteY = Math.Max(pipeline.Libraries.Count, 1) * RowHeight + SatelliteBandGap;
        positions["compute"] = new RegistrationPosition(SourceColumnX, satelliteY);
        if (pipeline.Notifications.Count > 0)
        {
            positions["notifications"] = new RegistrationPosition(PipelineColumnX, satelliteY);
        }

        return positions;
    }
}
