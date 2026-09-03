namespace EtAlii.Adp.Diagram.Databricks;

/// <summary>
/// One deployment target of a bundle, with the resources it overrides - the override edges the
/// bundle diagram draws from target frame to resource (Requirement 3.3).
/// </summary>
/// <param name="Name">The target's key under <c>targets:</c>.</param>
/// <param name="Mode">Its <c>mode:</c> as written - <c>development</c>, <c>production</c> - or empty.</param>
/// <param name="IsDefault">Whether the target says <c>default: true</c>.</param>
/// <param name="Overrides">The resources this target overrides, as kind/key pairs.</param>
/// <param name="Lines">The lines that declare the target.</param>
public sealed record BundleTarget(
    string Name,
    string Mode,
    bool IsDefault,
    IReadOnlyList<BundleResource> Overrides,
    LineRange Lines);
