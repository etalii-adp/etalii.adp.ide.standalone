namespace EtAlii.Adp.Diagram.AnsibleStructure;

/// <summary>Every rule's stable id, prefixed with the module's short name as core expects.</summary>
public static class AnsibleRules
{
    /// <summary>A playbook or a <c>meta/main.yml</c> names a role with no folder under <c>roles/</c>.</summary>
    public const string RoleMissing = "ansible.role-missing";

    /// <summary>An <c>import_playbook</c> or <c>include_tasks</c> names a file that is not there.</summary>
    public const string DanglingImport = "ansible.dangling-import";

    /// <summary>A play's <c>hosts:</c> pattern matches no group any inventory defines.</summary>
    public const string UnmatchedHosts = "ansible.unmatched-hosts";

    /// <summary>A file in the model does not parse, reported in the parser's own words.</summary>
    public const string UnreadableYaml = "ansible.unreadable-yaml";

    /// <summary>A folder under <c>roles/</c> holds no canonical content at all.</summary>
    public const string EmptyRole = "ansible.empty-role";
}
