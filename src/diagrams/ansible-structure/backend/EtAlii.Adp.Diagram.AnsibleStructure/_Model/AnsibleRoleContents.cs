namespace EtAlii.Adp.Diagram.AnsibleStructure;

/// <summary>
/// Which of a role's canonical subfolders exist and how full they are - so a hollow role is
/// visibly hollow rather than looking like any other box (Requirement 4.2).
/// </summary>
public sealed record AnsibleRoleContents(
    int TaskFiles,
    int Handlers,
    int Templates,
    int Files,
    int Defaults,
    int Vars,
    bool HasMeta,
    bool HasLibrary)
{
    /// <summary>
    /// Whether the folder holds no canonical content at all. Ansible would find nothing to run
    /// here, which is what <c>ansible.empty-role</c> warns about (Requirement 9.2).
    /// </summary>
    public bool IsHollow =>
        TaskFiles == 0 && Handlers == 0 && Templates == 0 && Files == 0 &&
        Defaults == 0 && Vars == 0 && !HasMeta && !HasLibrary;
}
