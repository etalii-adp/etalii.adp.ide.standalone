namespace EtAlii.Adp.Hierarchy;

/// <summary>
/// What the explorer's icon color says about an entry's relationship to ADP's diagram and
/// editor routing (small-refinements Requirement 3): registered, potential, or nothing at all.
/// </summary>
/// <remarks>
/// Zero means neutral on purpose: the proto field this maps onto is added to an existing
/// message, and proto3 defaults an absent enum to zero - so an older peer's silence and a
/// genuine "ADP has nothing to say about this file" read identically, which is exactly the
/// desired degradation.
/// </remarks>
public enum EntryDiagramState
{
    /// <summary>Neutral: no registration governs the entry and nothing claims it.</summary>
    Unspecified = 0,

    /// <summary>A registration exists for the entry (or the entry is one itself).</summary>
    Registered = 1,

    /// <summary>No registration yet, but a diagram type or a non-fallback editor claims it.</summary>
    Potential = 2,
}
