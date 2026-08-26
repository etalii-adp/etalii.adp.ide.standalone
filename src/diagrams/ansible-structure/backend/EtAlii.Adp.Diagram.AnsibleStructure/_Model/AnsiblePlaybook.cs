namespace EtAlii.Adp.Diagram.AnsibleStructure;

/// <summary>A playbook file: its plays, and the playbooks it imports.</summary>
/// <remarks>
/// <paramref name="Imports"/> is separate from the plays rather than inside one because Ansible
/// puts it there: <c>- import_playbook: other.yml</c> is an entry in the file's top-level list,
/// a sibling of the plays rather than something a play contains.
/// </remarks>
/// <param name="RelativePath">The file, relative to the diagram's folder.</param>
/// <param name="Name">The file name, which is what a reader calls this playbook.</param>
/// <param name="Plays">Its plays, in declaration order.</param>
/// <param name="Imports">Its <c>import_playbook:</c> entries, in declaration order.</param>
public sealed record AnsiblePlaybook(
    string RelativePath,
    string Name,
    IReadOnlyList<AnsiblePlay> Plays,
    IReadOnlyList<AnsibleDirective> Imports);
