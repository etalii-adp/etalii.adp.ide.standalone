namespace EtAlii.Adp.Diagram.HelmChart;

/// <summary>
/// A dependency condition, resolved against the default <c>values.yaml</c> at read time - the
/// badge shows its current truth (Requirement 5.5).
/// </summary>
public enum ConditionState
{
    /// <summary>The dependency declares no condition.</summary>
    None,

    /// <summary>The path exists and reads true.</summary>
    On,

    /// <summary>The path exists and reads false.</summary>
    Off,

    /// <summary>The path does not exist in the default values - what Requirement 10.6 warns about.</summary>
    Missing,

    /// <summary>The path exists but its value is not a boolean, or the values file itself did not parse.</summary>
    Unknown,
}
