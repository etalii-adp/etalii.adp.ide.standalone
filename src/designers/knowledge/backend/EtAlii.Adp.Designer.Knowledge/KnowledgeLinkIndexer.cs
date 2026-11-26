using EtAlii.Adp.Links;
using EtAlii.Adp.Specification.Fbl.Links;
using EtAlii.Adp.Specification.Fbl.Planning;

namespace EtAlii.Adp.Designer.Knowledge;

/// <summary>
/// The link index's reader for knowledge files (FBL 17.3; knowledge.md, section 9): a file of one of its formats read as the
/// designer reads it, held by its rows, each named by the file's title property, and by the links its relations hold, their
/// targets and their cells, a cell's link shown and opened at its row. A file that is not a knowledge file, or cannot be read, is held by nothing.
/// </summary>
internal sealed class KnowledgeLinkIndexer : ILinkIndexer
{
    private static readonly string[] _extensions = [".yaml", ".yml", ".json", ".xml"];

    /// <inheritdoc />
    public string Origin => Designer.Origin;

    /// <inheritdoc />
    public bool Claims(string path) => _extensions.Any(extension => path.EndsWith(extension, StringComparison.OrdinalIgnoreCase));

    /// <inheritdoc />
    public IndexedDocument? Index(string path, byte[] bytes)
    {
        if (Readable(path, bytes) is not { } body)
        {
            return null;
        }

        var table = body.Table;
        var title = table.Properties.FirstOrDefault(property => property.IsTitle);
        List<IndexedElement> rows = [.. table.Rows.Select(row => new IndexedElement(row.Id, Named(title, row)))];
        List<HeldLink> links = [];
        foreach (var relation in table.Properties.Where(property => property.Type is KnowledgeRelationType && KnowledgeLinks.IsLink(property.TargetFile)))
        {
            links.Add(new HeldLink(relation.Id, relation.TargetFile));
        }

        foreach (var row in table.Rows)
        {
            foreach (var cell in row.Cells.Where(cell => cell.Key == "rows"))
            {
                links.AddRange(cell.Written.Where(KnowledgeLinks.IsLink).Select(text => new HeldLink(KnowledgeEdits.CellId(row.Id, cell.PropertyId), text) { Element = row.Id }));
            }
        }

        return new IndexedDocument(path, table.DocumentId.Length > 0 ? table.DocumentId : null, title?.Name, rows, links) { Origin = Origin };
    }

    /// <inheritdoc />
    public byte[]? Renewed(string path, byte[] bytes)
    {
        if (Readable(path, bytes) is not { ReadOnlyReason.Length: 0 } body)
        {
            return null;
        }

        (byte[]? renewed, _) = body.Change([new ModelChange.SetDocumentId(KnowledgeLinkConversion.NewDocumentId())]);
        return renewed;
    }

    /// <summary>The knowledge body <paramref name="bytes"/> hold, or null when they hold none that can be read.</summary>
    private static KnowledgeBody? Readable(string path, byte[] bytes) =>
        KnowledgeBody.Read(bytes, Path.GetFileName(path)) is { Unreadable.Length: 0 } body && body.Model.Elements.Any(element => element.Type == "Table") ? body : null;

    /// <summary>A row's name under the file's title property: the property's name and the row's title, where it has one.</summary>
    private static List<KeyValuePair<string, string>> Named(KnowledgeProperty? title, KnowledgeRow row) =>
        title is not null && row.Cells.FirstOrDefault(cell => cell.PropertyId == title.Id && cell.Key == KnowledgeValueTypes.Text.CellKey)?.Values.FirstOrDefault() is { Length: > 0 } name
            ? [new KeyValuePair<string, string>(title.Name, name)]
            : [];
}
