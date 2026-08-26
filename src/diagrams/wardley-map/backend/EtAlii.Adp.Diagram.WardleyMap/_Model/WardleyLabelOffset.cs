namespace EtAlii.Adp.Diagram.WardleyMap;

/// <summary>
/// A `label [dx, dy]` offset, in pixels rather than map coordinates (Requirement 5.5).
/// </summary>
/// <remarks>
/// Pixels is the format's own choice and an odd one, since every other number in an `.owm` is a
/// 0..1 coordinate. ADP preserves it as written rather than converting it to something more
/// consistent, because a round trip has to give the author's file back.
/// </remarks>
public sealed record WardleyLabelOffset(double X, double Y);
