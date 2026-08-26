namespace EtAlii.Adp.Diagram.WardleyMap;

/// <summary>
/// The mime-style element types this module puts on the wire, all under `wardley/map`
/// (Requirement 10.2).
/// </summary>
/// <remarks>
/// The type tells the canvas what to draw without it having to inspect the payload, and it is
/// what makes a Wardley element distinguishable from a mindmap node on the same core stream.
/// The `+suffix` shape follows `freeplane/mindmap+node`.
/// </remarks>
public static class WardleyElementTypes
{
    /// <summary>A component, anchor or submap. Which one is in the payload's kind.</summary>
    public const string Element = "wardley/map+element";

    /// <summary>A link between two elements.</summary>
    public const string Link = "wardley/map+link";

    /// <summary>Free text at a position.</summary>
    public const string Note = "wardley/map+note";

    /// <summary>A numbered annotation, carrying every position it is pinned at.</summary>
    public const string Annotation = "wardley/map+annotation";

    /// <summary>An accelerator or deaccelerator.</summary>
    public const string Accelerator = "wardley/map+accelerator";

    /// <summary>A pioneers, settlers or town-planners region.</summary>
    public const string Attitude = "wardley/map+attitude";

    /// <summary>
    /// The map-level element carrying the evolution axis: the four stages and their boundaries,
    /// so the client draws bands it is told about rather than constants it holds
    /// (Requirement 8.2).
    /// </summary>
    public const string EvolutionAxis = "wardley/map+evolution-axis";

    /// <summary>The id of the single evolution-axis element. Fixed, because there is exactly one per map.</summary>
    public const string EvolutionAxisId = "wardley-evolution-axis";
}
