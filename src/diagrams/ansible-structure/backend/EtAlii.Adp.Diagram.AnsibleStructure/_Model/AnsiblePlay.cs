namespace EtAlii.Adp.Diagram.AnsibleStructure;

/// <summary>One play within a playbook: what it runs, and where.</summary>
/// <param name="Name">Its <c>name:</c>, or empty - Ansible does not require one.</param>
/// <param name="Hosts">Its <c>hosts:</c> pattern as written, or empty when it declares none.</param>
/// <param name="Index">Its position in the playbook, which is what makes its element id stable.</param>
/// <param name="Line">The 1-based line the play starts on.</param>
/// <param name="Directives">Its <c>roles:</c>, <c>include_role</c> and <c>import_role</c> entries, in declaration order.</param>
public sealed record AnsiblePlay(
    string Name,
    string Hosts,
    int Index,
    uint Line,
    IReadOnlyList<AnsibleDirective> Directives);
