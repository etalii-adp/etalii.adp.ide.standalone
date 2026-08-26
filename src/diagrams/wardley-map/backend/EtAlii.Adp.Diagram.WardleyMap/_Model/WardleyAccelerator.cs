namespace EtAlii.Adp.Diagram.WardleyMap;

/// <summary>
/// An `accelerator` or `deaccelerator` at a position on the map (Requirement 6.5) - something
/// pushing components along the evolution axis, or holding them back.
/// </summary>
/// <param name="Name">What the force is called.</param>
/// <param name="Position">Where it sits, in the document's own axis order.</param>
/// <param name="Line">The 1-based line that declares it.</param>
/// <param name="IsDeaccelerator">
/// True for `deaccelerator`. One record with a flag rather than two types, because that is how
/// the reference parser models it - both go in one `accelerators` collection distinguished by
/// a `deaccelerator` boolean - and splitting them here would invent a difference the format
/// does not make.
/// </param>
public sealed record WardleyAccelerator(
    string Name,
    WardleyCoordinate Position,
    uint Line,
    bool IsDeaccelerator = false);
