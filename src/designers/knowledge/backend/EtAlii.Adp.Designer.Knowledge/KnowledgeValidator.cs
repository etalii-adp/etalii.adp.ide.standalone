using EtAlii.Adp.Designer.TableModel;
using EtAlii.Adp.Specification.Fbl;

namespace EtAlii.Adp.Designer.Knowledge;

/// <summary>
/// What a knowledge file has that the designer cannot accept as it is, each as a finding at its
/// place: one code per condition of the specification's list (<c>knowledge.md</c>, <i>Findings</i>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Reading never fails on content, and reporting never changes it.</b> Everything readable is
/// shown; what is reported stays in the file exactly as it is, to be put right by its author -
/// here or in any other program - and is still there after any edit of anything else.
/// </para>
/// <para>
/// <b>Every condition is looked for, every time.</b> A file with two things wrong reports two: a
/// validator that stopped at the first would make its author find them one save at a time.
/// </para>
/// </remarks>
internal static class KnowledgeValidator
{
    public const string NoTitle = "knowledge.no-title";
    public const string SeveralTitles = "knowledge.several-titles";
    public const string TitleNotText = "knowledge.title-not-text";
    public const string DuplicatePropertyName = "knowledge.duplicate-property-name";
    public const string UnknownProperty = "knowledge.unknown-property";
    public const string ValueOfAnotherType = "knowledge.value-of-another-type";
    public const string UnknownOption = "knowledge.unknown-option";
    public const string RelationOverLimit = "knowledge.relation-over-limit";
    public const string ViewNamesMissingProperty = "knowledge.view-names-missing-property";
    public const string NoView = "knowledge.no-view";
    public const string UnknownType = "knowledge.unknown-type";
    public const string ParentCycle = "knowledge.parent-cycle";
    public const string MissingId = "knowledge.missing-id";
    public const string UnresolvedTarget = "knowledge.unresolved-target";
    public const string UnknownKey = "knowledge.unknown-key";
    public const string NewerVersion = "knowledge.newer-version";
    public const string UnresolvedRow = "knowledge.unresolved-row";

    /// <summary>The element types whose entries carry an id of their own in the file.</summary>
    internal static readonly string[] Identified = ["Property", "Option", "View", "Row"];

    /// <param name="table">The table as read.</param>
    /// <param name="model">The reading it was made from, for what only the entries themselves say: whether an id is stored.</param>
    /// <param name="targetOf">What a relation property points at, or null where relations are not resolved and nothing is said of them.</param>
    public static List<TableFinding> Validate(KnowledgeTable table, FblModel model, Func<KnowledgeProperty, KnowledgeTarget?>? targetOf = null)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(model);

        List<TableFinding> findings = [];

        var properties = new Dictionary<string, KnowledgeProperty>(StringComparer.Ordinal);
        foreach (var property in table.Properties)
        {
            properties.TryAdd(property.Id, property);
        }

        ValidateProperties(table, findings);
        ValidateCells(table, properties, findings);
        ValidateRelations(table, targetOf, findings);
        ValidateViews(table, properties, findings);
        ValidateParents(table, findings);
        ValidateIds(model, findings);

        return findings;
    }

    // ---- the table and its properties ----

    private static void ValidateProperties(KnowledgeTable table, List<TableFinding> findings)
    {
        var titles = table.Properties.Where(property => property.IsTitle).ToList();
        if (titles.Count == 0 && (table.Properties.Count > 0 || table.Rows.Count > 0))
        {
            Warn(findings, NoTitle, "The table has no title property, so its rows have no name.");
        }

        foreach (var extra in titles.Skip(1))
        {
            Warn(findings, SeveralTitles, $"'{extra.Name}' is marked as the title, and so is '{titles[0].Name}'; the first is used.", columnId: extra.Id);
        }

        foreach (var title in titles.Where(title => title.Type is not KnowledgeTextType))
        {
            Error(findings, TitleNotText, $"The title property '{title.Name}' is a {title.ValueType}; a title is text.", columnId: title.Id);
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in table.Properties)
        {
            if (!names.Add(property.Name))
            {
                Error(findings, DuplicatePropertyName, $"Another property is already called '{property.Name}'.", columnId: property.Id);
            }

            if (!property.Type.IsKnown)
            {
                Warn(findings, UnknownType, $"'{property.Name}' has the type '{property.ValueType}', which is none this application knows; its values are shown and cannot be changed here.", columnId: property.Id);
            }
        }
    }

    // ---- the cells ----

    private static void ValidateCells(KnowledgeTable table, Dictionary<string, KnowledgeProperty> properties, List<TableFinding> findings)
    {
        foreach (var row in table.Rows)
        {
            foreach (var cell in row.Cells)
            {
                if (!properties.TryGetValue(cell.PropertyId, out var property))
                {
                    Warn(findings, UnknownProperty, $"This cell names a property the table does not have ('{cell.PropertyId}'); it is kept as it is.", row.Id, cell.PropertyId);
                    continue;
                }

                ValidateCell(row, cell, property, findings);
            }
        }
    }

    private static void ValidateCell(KnowledgeRow row, KnowledgeCell cell, KnowledgeProperty property, List<TableFinding> findings)
    {
        var type = property.Type;
        if (cell.Values.Count == 0 || !type.IsKnown)
        {
            return;
        }

        if (cell.Key != type.CellKey)
        {
            Warn(findings, ValueOfAnotherType, $"This value is not a {property.ValueType}; it is kept as it is.", row.Id, property.Id);
            return;
        }

        if (type.HoldsOptions)
        {
            foreach (var value in cell.Values.Where(value => property.Options.All(option => option.Id != value)))
            {
                Warn(findings, UnknownOption, $"This value names an option '{property.Name}' does not have ('{value}'); it is kept as it is.", row.Id, property.Id);
            }
        }

        if (property is { Type: KnowledgeRelationType, Limit: "one" } && cell.Values.Count > 1)
        {
            Warn(findings, RelationOverLimit, $"'{property.Name}' relates to one row, and this cell names {cell.Values.Count}; they are kept as they are.", row.Id, property.Id);
        }
    }

    // ---- relations: a target that is not there, and a row that is not in it. The value stays either way. ----

    private static void ValidateRelations(KnowledgeTable table, Func<KnowledgeProperty, KnowledgeTarget?>? targetOf, List<TableFinding> findings)
    {
        foreach (var relation in table.Properties.Where(property => property.Type is KnowledgeRelationType && targetOf is not null))
        {
            var target = targetOf!(relation);
            if (target?.Table is not { } related)
            {
                Warn(findings, UnresolvedTarget, $"'{relation.Name}' relates to a table that cannot be shown: {target?.Problem ?? "it names no file."} Its values are kept as they are.", columnId: relation.Id);
                continue;
            }

            if (relation.IsComputed)
            {
                continue;
            }

            var there = related.Rows.Select(row => row.Id).ToHashSet(StringComparer.Ordinal);
            foreach (var row in table.Rows)
            {
                foreach (var value in row.Cells.FirstOrDefault(cell => cell.PropertyId == relation.Id && cell.Key == "rows")?.Values.Where(value => !there.Contains(value)) ?? [])
                {
                    Warn(findings, UnresolvedRow, $"This value names a row that is not in {(related.Name.Length > 0 ? related.Name : "the table it relates to")} ('{value}'); it is kept as it is.", row.Id, relation.Id);
                }
            }
        }
    }

    // ---- the views ----

    private static void ValidateViews(KnowledgeTable table, Dictionary<string, KnowledgeProperty> properties, List<TableFinding> findings)
    {
        if (table.Views.Count == 0)
        {
            Warn(findings, NoView, "The table has no view; it is shown as if it had one that changes nothing.");
        }

        foreach (var view in table.Views)
        {
            var missing = view.Columns.Select(column => column.PropertyId)
                .Concat(view.Sorts.Select(sort => sort.PropertyId))
                .Concat(Conditions(view.Filter).Select(condition => condition.PropertyId))
                .Append(view.GroupBy)
                .Append(view.Freeze)
                .Where(id => id.Length > 0 && !properties.ContainsKey(id))
                .Distinct(StringComparer.Ordinal);
            foreach (var id in missing)
            {
                Warn(findings, ViewNamesMissingProperty, $"The view '{view.Name}' has a setting for a property the table does not have ('{id}'); the view opens without it.");
            }
        }
    }

    // ---- rows that are their own ancestors ----

    private static void ValidateParents(KnowledgeTable table, List<TableFinding> findings)
    {
        if (table.Properties.FirstOrDefault(property => property.IsParent) is not { } parent)
        {
            return;
        }

        var under = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var row in table.Rows)
        {
            if (row.Cells.FirstOrDefault(candidate => candidate.PropertyId == parent.Id) is { Key: "rows", Values.Count: > 0 } cell)
            {
                under.TryAdd(row.Id, cell.Values[0]);
            }
        }

        foreach (var row in table.Rows.Where(row => under.ContainsKey(row.Id)))
        {
            // Walk up from the row; coming back to it within as many steps as there are rows is a circle.
            var at = under[row.Id];
            for (var steps = 0; steps <= under.Count && at != row.Id && under.TryGetValue(at, out var next); steps++)
            {
                at = next;
            }

            if (at == row.Id)
            {
                Error(findings, ParentCycle, $"This row is its own ancestor through '{parent.Name}'.", row.Id, parent.Id);
            }
        }
    }

    // ---- entries without an id ----

    private static void ValidateIds(FblModel model, List<TableFinding> findings)
    {
        foreach (var element in model.Elements.Where(element => !element.IdIsStored && Identified.Contains(element.Type)))
        {
            findings.Add(new TableFinding(
                MissingId,
                TableFindingSeverity.Info,
                $"A {element.Type.ToLowerInvariant()} on line {element.Line} has no id; it is shown, and anything that names it cannot until it has one.",
                element.Type == "Row" ? element.Id : "",
                element.Type == "Property" ? element.Id : ""));
        }
    }

    private static void Error(List<TableFinding> findings, string code, string message, string rowId = "", string columnId = "") => findings.Add(new TableFinding(code, TableFindingSeverity.Error, message, rowId, columnId));

    private static void Warn(List<TableFinding> findings, string code, string message, string rowId = "", string columnId = "") => findings.Add(new TableFinding(code, TableFindingSeverity.Warning, message, rowId, columnId));

    private static IEnumerable<KnowledgeCondition> Conditions(KnowledgeFilterGroup group) => group.Items.SelectMany(item => item switch
    {
        KnowledgeFilterGroup nested => Conditions(nested),
        KnowledgeCondition condition => [condition],
        _ => Enumerable.Empty<KnowledgeCondition>(),
    });
}
