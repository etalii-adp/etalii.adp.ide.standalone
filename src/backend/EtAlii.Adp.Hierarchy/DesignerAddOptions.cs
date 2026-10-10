using EtAlii.Adp.Context;
using EtAlii.Adp.Designer;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Hierarchy;

/// <summary>
/// The designer family's part of the Add dialog: a designer type is an entry of the same
/// option tree the diagram types are in, with the formats a document can be created in as the
/// choices under it (knowledge-designer Requirements 10.4 and 2.4).
/// </summary>
/// <remarks>
/// Not a context action provider of its own. An action is owned by the first provider that
/// reports its id, so a second provider reporting <c>hierarchy.add</c> would add a second Add to
/// the menu and never be asked to commit; and a designer type is to be offered where the other
/// tool types are. <see cref="AddDiagramContextActionProvider"/> therefore asks this class for
/// the options it adds and for what a chosen one creates.
/// <para>
/// The format is part of the choice and of nothing else: no option, here or anywhere, changes
/// the format of a document that exists (Requirement 2.5).
/// </para>
/// </remarks>
public sealed class DesignerAddOptions
{
    /// <summary>Separates the designer type from the format's extension in an option id. No origin holds it.</summary>
    private const char FormatSeparator = '@';

    private readonly IDesignerDefinitionCatalog _catalog;
    private readonly DesignerDocumentTemplates _templates;

    public DesignerAddOptions(IDesignerDefinitionCatalog catalog, DesignerDocumentTemplates templates)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(templates);
        _catalog = catalog;
        _templates = templates;
    }

    /// <summary>
    /// The designer types a document can be added of: those that declare a format and whose
    /// module registered a template. A type without either is not offered, rather than offered
    /// and refused on commit.
    /// </summary>
    private IEnumerable<DesignerDefinition> Offered =>
        _catalog.All.Where(definition => definition.Formats.Count > 0 && _templates.Find(definition.Origin) is not null);

    /// <summary>Whether any designer type can be added.</summary>
    public bool Any => Offered.Any();

    /// <summary>
    /// <paramref name="tree"/> with the designer types added under their vendors, each vendor's
    /// entries by label as the diagram types are.
    /// </summary>
    /// <param name="tree">The option tree of the diagram types: one group per vendor.</param>
    /// <param name="folder">The folder a document would be created in, for the suggested name.</param>
    public IReadOnlyList<ContextOptionNode> AddTo(IReadOnlyList<ContextOptionNode> tree, string folder)
    {
        ArgumentNullException.ThrowIfNull(tree);

        var vendors = tree.ToDictionary(node => node.Id, StringComparer.Ordinal);
        foreach (var byVendor in Offered.GroupBy(definition => VendorOf(definition.Origin), StringComparer.Ordinal))
        {
            var existing = vendors.GetValueOrDefault(byVendor.Key);
            var children = (existing?.Children ?? [])
                .Concat(byVendor.Select(definition => Entry(definition, folder)))
                .OrderBy(node => node.Label, StringComparer.Ordinal)
                .ToArray();
            vendors[byVendor.Key] = existing is null
                ? new ContextOptionNode(byVendor.Key, byVendor.Key, Selectable: false, Children: children)
                : existing with { Children = children };
        }

        return vendors.Values.OrderBy(node => node.Id, StringComparer.Ordinal).ToArray();
    }

    /// <summary>
    /// The designer type and format an option id names, or null when it names neither: a diagram
    /// type's id, or a stale one.
    /// </summary>
    public (DesignerDefinition Definition, DesignerFormat Format)? Resolve(string optionId)
    {
        ArgumentNullException.ThrowIfNull(optionId);

        var separator = optionId.LastIndexOf(FormatSeparator);
        if (separator < 0)
        {
            return null;
        }

        var origin = optionId[..separator];
        var extension = optionId[(separator + 1)..];
        var definition = Offered.FirstOrDefault(candidate => string.Equals(candidate.Origin, origin, StringComparison.Ordinal));
        var format = definition?.Formats.FirstOrDefault(candidate => string.Equals(candidate.Extension, extension, StringComparison.Ordinal));
        return definition is null || format is null ? null : (definition, format);
    }

    /// <summary>
    /// The command that creates a document named <paramref name="baseName"/> in
    /// <paramref name="folder"/>: the body from the type's template for the format, and the
    /// registration beside it holding the origin and nothing else. Null with a reason when it
    /// cannot be created.
    /// </summary>
    /// <remarks>
    /// The registration names its body in a <c>body:</c> line, always. The two files share a
    /// base name, but a designer's body has one of several extensions, and a registration that
    /// named none would take another file of that name - a <c>cities.yaml</c> beside a table
    /// created as <c>cities.json</c> - for its body. It carries no layout; a designer has none,
    /// and all it stores is in the body (Requirement 2.2).
    /// </remarks>
    public (CreateDiagramFileCommand? Command, string Error) Plan(DesignerDefinition definition, DesignerFormat format, string folder, string baseName)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(format);

        var bodyName = baseName + format.Extension;
        var bodyPath = IoPath.Combine(folder, bodyName);
        if (File.Exists(bodyPath) || Directory.Exists(bodyPath))
        {
            return (null, $"An item named '{bodyName}' already exists in this folder.");
        }

        var content = _templates.Find(definition.Origin)?.Create(format, bodyName);
        return content is null
            ? (null, $"A {definition.Title} document cannot be created as {format.Title}: its module is incomplete.")
            : (new CreateDiagramFileCommand(folder, DiagramFileName.WithExtension(baseName), definition.Origin, bodyName, content, NamesBody: true), "");
    }

    /// <summary>
    /// One designer type: a heading that cannot itself be chosen, with a choice per format. A
    /// type with one format is still shown this way, so what is being created is never implied.
    /// </summary>
    private static ContextOptionNode Entry(DesignerDefinition definition, string folder)
    {
        var suggested = DiagramFileName.Suggest(TypeOf(definition.Origin), folder);
        return new ContextOptionNode(
            definition.Origin,
            definition.Title,
            Selectable: false,
            Children: [.. definition.Formats.Select(format => new ContextOptionNode(
                $"{definition.Origin}{FormatSeparator}{format.Extension}",
                format.Title,
                Selectable: true,
                SuggestedValue: suggested,
                Description: definition.Description,
                Icon: definition.Icon))],
            Description: definition.Description,
            Icon: definition.Icon);
    }

    private static string VendorOf(string origin) => origin.Split('/')[0];

    private static string TypeOf(string origin) => origin[(origin.IndexOf('/') + 1)..].Replace('/', '-');
}
