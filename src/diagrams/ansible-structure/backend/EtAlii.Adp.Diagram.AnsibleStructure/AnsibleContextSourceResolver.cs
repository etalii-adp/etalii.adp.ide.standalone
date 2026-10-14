using EtAlii.Adp.Documents;
using EtAlii.Adp.Hierarchy;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Diagram.AnsibleStructure;

/// <summary>
/// Makes an Ansible node or edge selectable: resolves an <c>element_id</c> against the project whose
/// registration the selection sits in, verifies the element really is in that project, and fills in
/// the detail a consumer shows (Requirement 8.2).
/// </summary>
/// <remarks>
/// <para>
/// <b>The steps every resolver repeats are the base's</b> (code-structure FR-032). This says where
/// an Ansible diagram is read from and what its elements are called: the folder the registration
/// sits in, through the shared watched-folder store, and a node by its path and name, an edge by the
/// side that declares it (Requirement 8.3).
/// </para>
/// <para>
/// <b>The target is the registration</b>, as every diagram's is the file it was selected in; the
/// folder is one <c>GetDirectoryName</c> away, and the stored positions the canvas was drawn with
/// are in that file.
/// </para>
/// </remarks>
public sealed class AnsibleContextSourceResolver(DiagramFileRouter router, IAnsibleProjectStore store, AnsibleElementMapper mapper)
    : DiagramSnapshotSourceResolver<AnsibleContextSourceResolver.Selected>(router)
{
    /// <summary>A selected diagram: its project, the graph derived from it, and the registration it was selected in.</summary>
    public sealed record Selected(AnsibleProject Project, AnsibleGraph Graph, string Registration);

    /// <inheritdoc />
    protected override DiagramOrigin Origin => Diagram.AnsibleStructure.Origin;

    /// <inheritdoc />
    protected override DiagramElementTexts Texts { get; } = new(
        "The selected file is not an Ansible structure diagram.",
        "Unknown element.",
        "New element",
        "New relation");

    /// <inheritdoc />
    protected override Selected Load(string bodyPath)
    {
        var project = store.GetOrLoad(FolderOf(bodyPath));
        return new Selected(project, AnsibleGraph.Derive(project), bodyPath);
    }

    /// <inheritdoc />
    protected override (IReadOnlyList<string> Path, string Text)? Describe(Selected snapshot, string elementId)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (snapshot.Graph.Node(elementId) is { } node)
        {
            return (Segments(node.RelativePath), node.Name);
        }

        // An edge is selectable too, and it answers as its declaring side (Requirement 8.3).
        return snapshot.Graph.Edges.FirstOrDefault(candidate => string.Equals(candidate.Id, elementId, StringComparison.Ordinal)) is { } edge
            ? (Segments(edge.Directive.DeclaredIn), $"{DirectiveLabel(edge)} {edge.Directive.Target}")
            : null;
    }

    /// <inheritdoc />
    /// <remarks>Drawn as the canvas draws it: the whole diagram, with the positions the registration stores.</remarks>
    protected override DiagramElement? Drawn(Selected snapshot, string elementId)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var stored = RegistrationLayout.Read(snapshot.Registration);
        return mapper.Visible(snapshot.Project, snapshot.Graph, DiagramViewport.Unbounded, stored)
            .FirstOrDefault(element => string.Equals(element.Id, elementId, StringComparison.Ordinal));
    }

    /// <inheritdoc />
    /// <remarks>
    /// After every change to the folder: a deleted role clears the selection, a renamed file
    /// changes the path (Requirement 8.2).
    /// </remarks>
    protected override IDisposable Follow(ShortGuid watchId, string bodyPath, string elementId, Action changed)
    {
        ArgumentNullException.ThrowIfNull(changed);
        return store.Watch(FolderOf(bodyPath), _ => changed());
    }

    /// <summary>
    /// The folder a registration marks. The <c>.adp</c> routes as its own body for a type with
    /// no extension, so the body is the registration itself.
    /// </summary>
    private static string FolderOf(string registrationPath) =>
        IoPath.GetDirectoryName(IoPath.GetFullPath(registrationPath))
        ?? throw new InvalidOperationException($"'{registrationPath}' has no folder to read.");

    /// <summary>The directive as the file spells it, for an edge's own label.</summary>
    private static string DirectiveLabel(AnsibleEdge edge) => edge.Directive.Kind switch
    {
        AnsibleDirectiveKind.Roles => "roles:",
        AnsibleDirectiveKind.ImportRole => "import_role:",
        AnsibleDirectiveKind.IncludeRole => "include_role:",
        AnsibleDirectiveKind.ImportPlaybook => "import_playbook:",
        AnsibleDirectiveKind.ImportTasks => "import_tasks:",
        AnsibleDirectiveKind.IncludeTasks => "include_tasks:",
        AnsibleDirectiveKind.Dependency => "dependencies:",
        _ => "",
    };

    private static string[] Segments(string relativePath) =>
        relativePath.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
}
