using EtAlii.Adp.Specification.Fbl.Links;

namespace EtAlii.Adp.Designer.Knowledge;

/// <summary>
/// The links a knowledge file holds (knowledge.md, section 9; FBL section 17.1): a relation names its target document by a
/// link without an element, <c>$&lt;document-id&gt;@&lt;file name&gt;</c>, and a cell holds a link to each related row,
/// <c>$&lt;document-id&gt;@&lt;file name&gt;#&lt;row id&gt;.&lt;title property&gt;=&lt;title&gt;</c>. A file made before links
/// names its target by a path and its rows by their ids, and both forms are read.
/// </summary>
internal static class KnowledgeLinks
{
    /// <summary>Whether a relation's target or a related row is written as a link rather than as a path or a bare id.</summary>
    public static bool IsLink(string text) => text.StartsWith('$');

    /// <summary>The id of the row a cell's value names: the element of its link, or the value itself in a file made before links.</summary>
    public static string RowIdOf(string value) => IsLink(value) && FblLink.Parse(value) is { ElementId: { } id } ? id : value;

    /// <summary>The document id a relation's target names as a link; empty for a path, or for a link that cannot be read.</summary>
    public static string DocumentIdOf(string targetFile) => IsLink(targetFile) && FblLink.Parse(targetFile) is { } link ? link.DocumentId : "";

    /// <summary>How a relation names the knowledge file at <paramref name="path"/> whose table is <paramref name="table"/>: by a link to its document id.</summary>
    public static string ToDocument(KnowledgeTable table, string path) => FblLink.ToDocument(table.DocumentId, Path.GetFileName(path)).Text;

    /// <summary>
    /// How a cell names the row <paramref name="rowId"/> of the knowledge file at <paramref name="path"/>: a link named by the
    /// file's title property and the row's title, or by the row's id alone where it has no title (knowledge.md, section 9).
    /// </summary>
    public static string ToRow(KnowledgeTable table, string path, string rowId)
    {
        var title = table.Properties.FirstOrDefault(property => property.IsTitle);
        var row = table.Rows.FirstOrDefault(candidate => candidate.Id == rowId);
        var name = title is null || row is null ? null : row.Cells.FirstOrDefault(cell => cell.PropertyId == title.Id && cell.Key == KnowledgeValueTypes.Text.CellKey)?.Values.FirstOrDefault();
        return name is { Length: > 0 }
            ? new FblLink(table.DocumentId, Path.GetFileName(path), rowId, title!.Name, name).Text
            : new FblLink(table.DocumentId, Path.GetFileName(path), rowId, null, null).Text;
    }

    /// <summary>
    /// How a cell of a relation whose target is <paramref name="target"/> writes the row <paramref name="rowId"/>: as a link when
    /// the relation names its target by one, which needs the target's document id; as the bare id in a relation made before links.
    /// </summary>
    public static string WrittenRow(KnowledgeProperty relation, KnowledgeTarget? target, string rowId) =>
        IsLink(relation.TargetFile) && target?.Table is { DocumentId.Length: > 0 } table ? ToRow(table, target.Path, rowId) : rowId;
}
