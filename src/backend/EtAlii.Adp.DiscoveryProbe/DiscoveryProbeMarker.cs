namespace EtAlii.Adp.DiscoveryProbe;

/// <summary>
/// The assembly's one type. Nothing uses it, and that is the point: an assembly that is
/// deployed yet reachable by no metadata reference is exactly what the discovery walk's
/// manifest seeding exists to find.
/// </summary>
public sealed class DiscoveryProbeMarker;
