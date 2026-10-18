using EtAlii.Adp.Diagram.Modules;
using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.Specification.Disl;
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
/// sits in, through the host's open folders (<see cref="AnsibleFolders"/>), and a node by its path and name, an edge by the
/// side that declares it (Requirement 8.3).
/// </para>
/// <para>
/// <b>It reads the definition's model of the folder</b> (ansible-structure-disl-fbl, Requirement 3.5): a
/// node's <c>path</c> and <c>name</c>, a relation's <c>declaredIn</c>, <c>targetAsWritten</c> and the
/// definition's label of its <c>directive</c>, and the payload the mapper draws from the same model.
/// </para>
/// <para>
/// <b>The target is the registration</b>, as every diagram's is the file it was selected in; the
/// folder is one <c>GetDirectoryName</c> away, and the stored positions the canvas was drawn with
/// are in that file.
/// </para>
/// </remarks>
public sealed class AnsibleContextSourceResolver(DiagramFileRouter router, AnsibleFolders folders, AnsibleElementMapper mapper)
    : DiagramSnapshotSourceResolver<AnsibleContextSourceResolver.Selected>(router)
{
    /// <summary>A selected diagram: the definition's model of its folder, and the registration it was selected in.</summary>
    public sealed record Selected(DislDiagram Diagram, string Registration);

    /// <inheritdoc />
    protected override ToolOrigin Origin => Diagram.AnsibleStructure.Origin;

    /// <inheritdoc />
    protected override DiagramElementTexts Texts { get; } = new(
        "The selected file is not an Ansible structure diagram.",
        "Unknown element.",
        "New element",
        "New relation");

    /// <inheritdoc />
    /// <remarks>Its folder store reads as it is asked, so the read is already done when this returns.</remarks>
    protected override ValueTask<Selected> LoadAsync(string bodyPath, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Load(bodyPath));

    /// <inheritdoc />
    protected override Selected Current(string bodyPath) => Load(bodyPath);

    private Selected Load(string bodyPath)
    {
        return new Selected(folders.GetOrLoad(FolderOf(bodyPath), bodyPath).Disl.Diagram, bodyPath);
    }

    /// <inheritdoc />
    protected override (IReadOnlyList<string> Path, string Text)? Describe(Selected snapshot, string elementId)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return snapshot.Diagram.ElementById(elementId) switch
        {
            null => null,
            { Type.IsRelation: false } node => (Segments(Text(node, "path")), Text(node, "name")),

            // An edge is selectable too, and it answers as its declaring side (Requirement 8.3).
            var relation => (Segments(Text(relation, "declaredIn")), $"{DirectiveLabel(relation)} {Text(relation, "targetAsWritten")}"),
        };
    }

    /// <inheritdoc />
    /// <remarks>Drawn as the canvas draws it: the whole diagram, with the positions the registration stores.</remarks>
    protected override DiagramElement? Drawn(Selected snapshot, string elementId)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var stored = RegistrationLayout.Read(snapshot.Registration);
        return mapper.Visible(snapshot.Diagram, DiagramViewport.Unbounded, stored)
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
        return folders.Watch(FolderOf(bodyPath), bodyPath, _ => changed());
    }

    /// <summary>
    /// The folder a registration marks. The <c>.adp</c> routes as its own body for a type with
    /// no extension, so the body is the registration itself.
    /// </summary>
    private static string FolderOf(string registrationPath) =>
        IoPath.GetDirectoryName(IoPath.GetFullPath(registrationPath))
        ?? throw new InvalidOperationException($"'{registrationPath}' has no folder to read.");

    /// <summary>
    /// The directive as the file spells it, for an edge's own label: the definition's label, as a key, so
    /// <c>import_role</c> reads <c>import_role:</c> and <c>roles:</c>, which is already one, stays as it is.
    /// </summary>
    private static string DirectiveLabel(DislElement relation)
    {
        var label = AnsibleDefinition.Label(relation, "directive");
        return label.EndsWith(':') ? label : label + ":";
    }

    private static string Text(DislElement element, string attribute) => element.ValueOf(attribute) as string ?? "";

    private static string[] Segments(string relativePath) =>
        relativePath.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
}
