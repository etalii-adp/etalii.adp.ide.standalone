namespace EtAlii.Adp.Diagram.AnsibleStructure;

/// <summary>The node kinds this diagram draws (Requirement 4.1).</summary>
public enum AnsibleNodeKind
{
    /// <summary>A playbook file.</summary>
    Playbook,

    /// <summary>One play within a playbook - drawn only when a playbook has more than one.</summary>
    Play,

    /// <summary>A folder under <c>roles/</c>.</summary>
    Role,

    /// <summary>A role's task file, drawn when something includes it.</summary>
    TaskFile,

    /// <summary>An inventory - an environment, or a root inventory file.</summary>
    Inventory,

    /// <summary>A <c>group_vars</c> or <c>host_vars</c> folder.</summary>
    VariableFolder,
}
