namespace EtAlii.Adp.Diagram.AnsibleStructure;

/// <summary>
/// Which Ansible directive declared a relationship. Five kinds rather than one, because
/// <c>roles:</c>, <c>import_playbook:</c>, <c>include_tasks:</c> and <c>dependencies:</c> mean
/// four different things to Ansible and would mean one thing to a reader if drawn alike
/// (Requirement 5).
/// </summary>
public enum AnsibleDirectiveKind
{
    /// <summary>A play's <c>roles:</c> list. Static: resolved before the run.</summary>
    Roles,

    /// <summary><c>import_role:</c>. Static.</summary>
    ImportRole,

    /// <summary><c>include_role:</c>. Dynamic: resolved during the run.</summary>
    IncludeRole,

    /// <summary><c>import_playbook:</c>, at a playbook's top level rather than inside a play. Static.</summary>
    ImportPlaybook,

    /// <summary><c>import_tasks:</c>, within a role's tasks. Static.</summary>
    ImportTasks,

    /// <summary><c>include_tasks:</c>, within a role's tasks. Dynamic.</summary>
    IncludeTasks,

    /// <summary>A <c>meta/main.yml</c> <c>dependencies:</c> entry. Role to role, in its own style.</summary>
    Dependency,
}
