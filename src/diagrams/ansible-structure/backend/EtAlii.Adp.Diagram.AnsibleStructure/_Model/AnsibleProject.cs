namespace EtAlii.Adp.Diagram.AnsibleStructure;

/// <summary>
/// One registered folder, read. The aggregate the graph, the layout, the mapper and the rules
/// all work from - so the diagram and the problems panel can never disagree about what is in
/// the folder, because they are looking at the same object.
/// </summary>
/// <remarks>
/// A record for its constructor and its printing, <b>not</b> for its equality: every collection
/// here is an <see cref="IReadOnlyList{T}"/>, and a record's generated equality compares those
/// by reference. Two reads of one unchanged folder are therefore never <c>Equal</c>. Nothing
/// relies on that today; anything that later wants "did this change?" has to compare contents
/// rather than the records, and should say so where it does.
/// </remarks>
/// <param name="FolderPath">The absolute folder the <c>.adp</c> sits in.</param>
/// <param name="Playbooks">Root-level and <c>playbooks/</c> files whose top level is a list of plays.</param>
/// <param name="Roles">Folders under <c>roles/</c>, hollow ones included.</param>
/// <param name="Inventories">Environments under <c>inventories/</c>, or a root inventory file.</param>
/// <param name="Annotations">
/// <c>ansible.cfg</c> and <c>requirements.yml</c>, relative to the folder. Shown on the project
/// node rather than as boxes of their own (Requirement 4.1).
/// </param>
/// <param name="Failures">
/// The files that would not parse. Kept on the model rather than thrown, so the rest of the
/// diagram still renders and each failure can be reported at its own file and line.
/// </param>
public sealed record AnsibleProject(
    string FolderPath,
    IReadOnlyList<AnsiblePlaybook> Playbooks,
    IReadOnlyList<AnsibleRole> Roles,
    IReadOnlyList<AnsibleInventory> Inventories,
    IReadOnlyList<string> Annotations,
    IReadOnlyList<AnsibleYamlFailure> Failures)
{
    /// <summary>Whether the folder held nothing this module recognises - a valid answer, not a fault (Requirement 9.4).</summary>
    public bool IsEmpty => Playbooks.Count == 0 && Roles.Count == 0 && Inventories.Count == 0;

    /// <summary>The role of that name, or null. Roles are named by their folder, so the lookup is ordinal.</summary>
    public AnsibleRole? Role(string name) =>
        Roles.FirstOrDefault(role => string.Equals(role.Name, name, StringComparison.Ordinal));
}
