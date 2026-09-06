using EtAlii.Adp.Common;
namespace EtAlii.Adp.Diagram.AnsibleStructure;

/// <summary>This diagram type's identity, cataloged in docs/diagrams.md as `ansible/structure`.</summary>
/// <remarks>
/// Two declarations here are unusual, and both are deliberate.
/// <para>
/// <b>No document extension.</b> Every other type names a sibling file that holds its body. This
/// one has none: the <c>.adp</c> registration is the whole of what ADP contributes, and the
/// diagram is read from the folder around it. `DiagramRouting` already models that - a bodyless
/// type routes to `DiagramRouted` with its registration as its own body.
/// </para>
/// <para>
/// <b><see cref="DiagramSubject.Folder"/>.</b> What makes the difference visible to core: it is
/// what hands this module's validator the folder to read, and what makes an edit to a file
/// beneath the registration revalidate this diagram rather than being ignored. Declaring it
/// alongside an extension would be a contradiction, and discovery refuses that pairing.
/// </para>
/// </remarks>
public static class Diagram
{
    /// <summary>
    /// This module's one type, named so the module's own registrations can say which type they
    /// serve without indexing into the array. Discovery reads <see cref="Definitions"/>; the
    /// module reads this.
    /// </summary>
    public static DiagramDefinition AnsibleStructure { get; } = new(
        new DiagramOrigin("ansible", "structure"),
        "Ansible project structure (read-only)",
        "What runs what, and where a value comes from: playbooks, roles, inventories and the "
        + "include, import and dependency edges between them, drawn from the folder as it is.",
        Icon: "mdi-sitemap",
        Subject: DiagramSubject.Folder,
        // Read-only, and the only type whose subject is a folder rather than a document.
        Build: builder => builder.Services.AddAnsibleStructure());

    /// <summary>What discovery reads. One entry: this module carries one notation.</summary>
    public static DiagramDefinition[] Definitions { get; } = [AnsibleStructure];
}
