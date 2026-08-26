namespace EtAlii.Adp.Diagram.AnsibleStructure;

/// <summary>
/// One group an inventory defines, and how many hosts it names. What a play's <c>hosts:</c>
/// pattern is matched against (Requirement 5.6).
/// </summary>
public sealed record AnsibleInventoryGroup(string Name, int HostCount);
