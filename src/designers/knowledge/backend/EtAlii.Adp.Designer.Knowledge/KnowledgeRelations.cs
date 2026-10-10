namespace EtAlii.Adp.Designer.Knowledge;

/// <summary>What a relation property points at: the table of its target file, or why there is none to show.</summary>
/// <param name="Path">The target file's full path.</param>
/// <param name="Table">The target's table, or null when the file is missing, cannot be read or is not a knowledge file.</param>
/// <param name="Problem">Why there is no table, as a sentence; empty when there is one.</param>
internal sealed record KnowledgeTarget(string Path, KnowledgeTable? Table, string Problem = "");

/// <summary>
/// Relations between knowledge files (knowledge-designer Requirement 5): where a relation points,
/// what its values are called, and the side of a two-way relation that holds no values of its own.
/// </summary>
/// <remarks>
/// <para>
/// <b>A relation stores the target's path relative to its own file, and its values are the
/// target's row ids.</b> Neither the target's name nor a row's title is ever copied into the
/// file that relates to it, so renaming a row over there changes nothing over here.
/// </para>
/// <para>
/// <b>One side holds the values.</b> The other side of a two-way relation is a property marked
/// computed, with no cells: what it shows is read from the side that holds them, each time, so the
/// two sides cannot disagree.
/// </para>
/// </remarks>
internal static class KnowledgeRelations
{
    /// <summary>What a relation to the file itself names as its target.</summary>
    public const string Self = ".";

    /// <summary>The full path a relation's target names, from the file the relation is in.</summary>
    public static string TargetPath(string bodyPath, string targetFile) =>
        targetFile is Self or "" ? Path.GetFullPath(bodyPath) : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(bodyPath)) ?? "", targetFile));

    /// <summary>How a file names another as a relation's target: its path from the file's own folder, with forward slashes; <see cref="Self"/> for itself.</summary>
    public static string TargetName(string bodyPath, string targetPath)
    {
        (string from, string to) = (Path.GetFullPath(bodyPath), Path.GetFullPath(targetPath));
        return string.Equals(from, to, StringComparison.OrdinalIgnoreCase)
            ? Self
            : Path.GetRelativePath(Path.GetDirectoryName(from) ?? "", to).Replace('\\', '/');
    }

    /// <summary>What a row is called: the text of its title property, or its id when it has none.</summary>
    public static string TitleOf(KnowledgeTable table, KnowledgeRow row)
    {
        var title = table.Properties.FirstOrDefault(property => property.IsTitle);
        var text = title is null ? null : row.Cells.FirstOrDefault(cell => cell.PropertyId == title.Id && cell.Key == "text")?.Values.FirstOrDefault();
        return text is { Length: > 0 } ? text : row.Id;
    }

    /// <summary>What a relation's value is called: the title of the row it names in the target, or empty when it names none.</summary>
    public static string LabelOf(KnowledgeTarget? target, string rowId) =>
        target?.Table?.Rows.FirstOrDefault(candidate => candidate.Id == rowId) is { } row ? TitleOf(target.Table, row) : "";

    /// <summary>
    /// The table with the computed sides of its two-way relations filled in: for each row, the rows
    /// of the other table whose relation names it. Nothing of this is in the file, and nothing of it
    /// is ever written.
    /// </summary>
    public static KnowledgeTable WithComputed(KnowledgeTable table, Func<KnowledgeProperty, KnowledgeTarget?> targetOf)
    {
        var computed = table.Properties.Where(property => property is { ValueType: "relation", IsComputed: true, Counterpart.Length: > 0 }).ToList();
        if (computed.Count == 0)
        {
            return table;
        }

        // For each computed property: which rows over there name which rows here.
        var related = new Dictionary<string, Dictionary<string, List<string>>>(StringComparer.Ordinal);
        foreach (var property in computed)
        {
            // A relation to the file itself is read from the table as it is shown, edits not yet written included.
            var other = property.TargetFile is Self or "" ? table : targetOf(property)?.Table;
            var byRow = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (var row in other?.Rows ?? [])
            {
                foreach (var value in row.Cells.FirstOrDefault(cell => cell.PropertyId == property.Counterpart && cell.Key == "rows")?.Values ?? [])
                {
                    if (!byRow.TryGetValue(value, out var naming))
                    {
                        naming = [];
                        byRow[value] = naming;
                    }

                    naming.Add(row.Id);
                }
            }

            related[property.Id] = byRow;
        }

        var rows = table.Rows.Select(row =>
        {
            var cells = row.Cells.Where(cell => !related.ContainsKey(cell.PropertyId)).ToList();
            foreach (var property in computed)
            {
                if (related[property.Id].TryGetValue(row.Id, out var naming))
                {
                    cells.Add(new KnowledgeCell(property.Id, naming, "rows"));
                }
            }

            return row with { Cells = cells };
        }).ToList();
        return table with { Rows = rows };
    }

    /// <summary>
    /// Whether making <paramref name="parentId"/> the parent of <paramref name="rowId"/> would make a
    /// row its own ancestor: the parent is the row itself, or has it somewhere above.
    /// </summary>
    public static bool WouldCycle(KnowledgeTable table, KnowledgeProperty parent, string rowId, string parentId)
    {
        var above = parentId;
        for (var steps = 0; steps <= table.Rows.Count; steps++)
        {
            if (above == rowId)
            {
                return true;
            }

            var next = table.Rows.FirstOrDefault(row => row.Id == above)?.Cells.FirstOrDefault(cell => cell.PropertyId == parent.Id && cell.Key == "rows")?.Values.FirstOrDefault();
            if (next is null)
            {
                return false;
            }

            above = next;
        }

        // A circle that was already there, and does not pass through this row.
        return false;
    }
}
