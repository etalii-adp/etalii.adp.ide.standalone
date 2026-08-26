namespace EtAlii.Adp.Diagram.AnsibleStructure;

/// <summary>A role under <c>roles/</c>: what it holds, what it includes, and what it depends on.</summary>
/// <param name="Name">The folder name, which is what a playbook writes to use it.</param>
/// <param name="RelativePath">The folder, relative to the diagram's folder.</param>
/// <param name="Contents">Which canonical subfolders exist, and how full.</param>
/// <param name="TaskFiles">Every task file in the role, whether or not anything includes it.</param>
/// <param name="Dependencies">Its <c>meta/main.yml</c> <c>dependencies:</c> entries.</param>
/// <param name="TaskIncludes">Its <c>include_tasks:</c> and <c>import_tasks:</c> entries, from anywhere in its tasks.</param>
public sealed record AnsibleRole(
    string Name,
    string RelativePath,
    AnsibleRoleContents Contents,
    IReadOnlyList<AnsibleTaskFile> TaskFiles,
    IReadOnlyList<AnsibleDirective> Dependencies,
    IReadOnlyList<AnsibleDirective> TaskIncludes);
