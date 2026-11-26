using System.Security.Cryptography;
using EtAlii.Adp.Specification.Fbl.Planning;

namespace EtAlii.Adp.Designer.Knowledge;

/// <summary>
/// The conversion of a knowledge file made before links (knowledge.md, section 9; spec 089, FR-026, R9; etalii.adp spec 092,
/// T028): what loading it finds to change, held and written with its first edit, in this file alone.
/// <list type="number">
/// <item>A file without a document id is given one, and its version becomes <c>0.3</c>.</item>
/// <item>Each relation whose target file has a document id names it by a link, and its cells' row ids become links to those
/// rows; a relation with a <c>counterpart</c> takes that property's name as its <c>backlinkName</c> instead.</item>
/// <item>A counterpart property of this file is removed once no relation in the old form names it.</item>
/// </list>
/// A relation whose target has no id yet, or is not there, is left as it is.
/// </summary>
internal static class KnowledgeLinkConversion
{
    /// <summary>The version a file is converted to.</summary>
    public const string Version = "0.3";

    private const string Base36 = "0123456789abcdefghijklmnopqrstuvwxyz";

    /// <summary>A new document id: 8 characters of base 36 (knowledge.des, <c>persistence.documentId</c>).</summary>
    public static string NewDocumentId() => RandomNumberGenerator.GetString(Base36, 8);

    /// <summary>
    /// The changes that convert the knowledge file at <paramref name="bodyPath"/>, whose table as read from disk is
    /// <paramref name="table"/>; empty when it is converted already.
    /// </summary>
    /// <param name="table">The table as the file holds it.</param>
    /// <param name="bodyPath">The file's path.</param>
    /// <param name="targetOf">What a relation of the file points at, read only.</param>
    /// <param name="newId">The document id given to a file that has none.</param>
    public static List<ModelChange> Of(KnowledgeTable table, string bodyPath, Func<KnowledgeProperty, KnowledgeTarget?> targetOf, Func<string> newId)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(targetOf);
        ArgumentNullException.ThrowIfNull(newId);

        List<ModelChange> changes = [];
        var self = table;
        if (table.DocumentId.Length == 0)
        {
            self = table with { DocumentId = newId() };
            changes.Add(new ModelChange.SetDocumentId(self.DocumentId));
            changes.Add(new ModelChange.Set(KnowledgeEdits.TableId, new Dictionary<string, object?> { ["version"] = Version }) { Converting = true });
        }

        foreach (var property in table.Properties.Where(property => property.Type is KnowledgeRelationType))
        {
            if (property.IsComputed)
            {
                if (!StillNamed(property, targetOf))
                {
                    changes.Add(new ModelChange.Remove(property.Id));
                }

                continue;
            }

            if (KnowledgeLinks.IsLink(property.TargetFile))
            {
                continue;
            }

            var target = property.TargetsItsOwnTable ? new KnowledgeTarget(Path.GetFullPath(bodyPath), self) : targetOf(property);
            if (target?.Table is not { DocumentId.Length: > 0 } related)
            {
                continue;
            }

            changes.Add(Converted(property, target.Path, related));
            changes.AddRange(Cells(table, property, target.Path, related));
        }

        return changes;
    }

    /// <summary>The relation naming its target by a link, with its counterpart's name as its backlink name.</summary>
    private static ModelChange.Set Converted(KnowledgeProperty relation, string targetPath, KnowledgeTable related)
    {
        var attributes = new Dictionary<string, object?> { ["targetFile"] = KnowledgeLinks.ToDocument(related, targetPath) };
        if (relation.Counterpart.Length > 0)
        {
            attributes["counterpart"] = null;
            if (related.Properties.FirstOrDefault(candidate => candidate.Id == relation.Counterpart) is { } counterpart)
            {
                attributes["backlinkName"] = counterpart.Name;
            }
        }

        return new ModelChange.Set(relation.Id, attributes);
    }

    /// <summary>Each related row a cell of the relation names, as a link to it: as many links after as there were ids before.</summary>
    private static IEnumerable<ModelChange> Cells(KnowledgeTable table, KnowledgeProperty relation, string targetPath, KnowledgeTable related)
    {
        foreach (var row in table.Rows)
        {
            if (row.Cells.FirstOrDefault(cell => cell.PropertyId == relation.Id && cell.Key == "rows") is not { } cell)
            {
                continue;
            }

            var cellId = KnowledgeEdits.CellId(row.Id, relation.Id);
            foreach ((string value, string written) in cell.Values.Zip(cell.Written.Count == cell.Values.Count ? cell.Written : cell.Values).Distinct())
            {
                if (!KnowledgeLinks.IsLink(written))
                {
                    yield return new ModelChange.Set($"{cellId}/{value}", new Dictionary<string, object?> { [relation.Type.StoredKey] = KnowledgeLinks.ToRow(related, targetPath, value) });
                }
            }
        }
    }

    /// <summary>
    /// Whether the relation a counterpart property of this file shows is still in the old form and names it back.
    /// A counterpart of a relation of this file to itself never is: that relation is converted in the same step.
    /// </summary>
    private static bool StillNamed(KnowledgeProperty counterpart, Func<KnowledgeProperty, KnowledgeTarget?> targetOf) =>
        !counterpart.TargetsItsOwnTable
        && targetOf(counterpart)?.Table?.Properties.FirstOrDefault(candidate => candidate.Id == counterpart.Counterpart) is { } relation
        && !KnowledgeLinks.IsLink(relation.TargetFile)
        && relation.Counterpart == counterpart.Id;
}
