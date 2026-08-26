namespace EtAlii.Adp.Diagram.AnsibleStructure;

/// <summary>
/// The five kinds of relationship this diagram draws. Five rather than one because Ansible
/// means five different things by them, and a reader shown one line style would learn that they
/// are the same thing (Requirement 5).
/// </summary>
public enum AnsibleEdgeKind
{
    /// <summary>A play lists a role - through <c>roles:</c>, <c>import_role</c> or <c>include_role</c>.</summary>
    UsesRole,

    /// <summary>A playbook imports another playbook.</summary>
    ImportsPlaybook,

    /// <summary>A role's tasks include or import another of its task files. Drawn within the role.</summary>
    IncludesTasks,

    /// <summary>A role's <c>meta/main.yml</c> declares another role as a dependency. Its own style, distinct from being listed by a playbook.</summary>
    DependsOn,

    /// <summary>A play's <c>hosts:</c> pattern matches a group an inventory defines.</summary>
    Targets,
}
