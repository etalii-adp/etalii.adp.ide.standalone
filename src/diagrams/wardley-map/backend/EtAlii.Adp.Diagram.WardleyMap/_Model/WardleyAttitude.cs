namespace EtAlii.Adp.Diagram.WardleyMap;

/// <summary>
/// A rectangular region of the map given an attitude - `pioneers [0.30, 0.20, 0.55, 0.45]`
/// (Requirement 6.4). Rendered behind the elements it covers.
/// </summary>
/// <param name="Kind">Pioneers, settlers or town planners.</param>
/// <param name="From">One corner, in the document's own axis order.</param>
/// <param name="To">The opposite corner.</param>
/// <param name="Line">The 1-based line that declares it.</param>
/// <remarks>
/// The four numbers are <b>`[visibility1, maturity1, visibility2, maturity2]`</b> - visibility
/// first, as everywhere else in this format. That was confirmed against the reference parser
/// rather than inferred: it reads them into `visibility`/`maturity`/`visibility2`/`maturity2`
/// in that order, and the reverse reading would have put every attitude region on the wrong
/// part of the map while still looking plausible.
/// </remarks>
public sealed record WardleyAttitude(
    WardleyAttitudeKind Kind,
    WardleyCoordinate From,
    WardleyCoordinate To,
    uint Line);
