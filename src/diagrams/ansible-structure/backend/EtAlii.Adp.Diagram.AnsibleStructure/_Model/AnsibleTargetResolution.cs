namespace EtAlii.Adp.Diagram.AnsibleStructure;

/// <summary>
/// What became of an edge's target. Three states rather than two, and the third is the point:
/// collapsing <see cref="Unresolvable"/> into <see cref="Missing"/> would report a missing role
/// for every parameterised role in every real repository (Requirement 3.4).
/// </summary>
public enum AnsibleTargetResolution
{
    /// <summary>The thing it names is in the folder.</summary>
    Resolved,

    /// <summary>The thing it names is not in the folder, and could have been. A problem.</summary>
    Missing,

    /// <summary>
    /// Its target is a Jinja expression, so what it names is not knowable without running
    /// Ansible. Shown as written and marked, never guessed at - and never reported as missing,
    /// because unknown is not wrong.
    /// </summary>
    Unresolvable,
}
