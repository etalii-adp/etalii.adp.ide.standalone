namespace EtAlii.Adp.Diagram.WardleyMap;

/// <summary>
/// The three attitudes a region of a map can be given (Requirement 6.4) - Wardley's pioneers,
/// settlers and town planners.
/// </summary>
public enum WardleyAttitudeKind
{
    /// <summary>Exploring the genesis end: high failure rate, high learning.</summary>
    Pioneers,

    /// <summary>Turning what pioneers found into something dependable.</summary>
    Settlers,

    /// <summary>Industrialising the commodity end.</summary>
    TownPlanners,
}
