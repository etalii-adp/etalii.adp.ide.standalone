namespace EtAlii.Adp.Designer.Knowledge;

/// <summary>
/// The groups of a view that is grouped by a property (knowledge-designer Requirement 6.6): which
/// there are, what each is called, and in which order the view has them. One place, so that what
/// the table draws, what the group menu lists and what a gesture moves are the same groups.
/// </summary>
internal static class KnowledgeGroups
{
    /// <summary>
    /// Every group of the grouping, hidden ones included, in the view's order: the groups its
    /// order names first, then the others as they come - an option each in the options' order,
    /// unticked and ticked, or a related row each as the rows name them - and last the group of
    /// rows without a value, which a checkbox has none of.
    /// </summary>
    /// <param name="labelOf">What a related row is called, by its id; null where relations are not resolved, and a row is then called by its id.</param>
    public static List<(string Key, string Label)> Of(KnowledgeTable table, KnowledgeView view, KnowledgeProperty grouped, Func<string, string>? labelOf = null)
    {
        List<(string Key, string Label)> natural = grouped.ValueType switch
        {
            "selection" or "multipleSelection" => [.. grouped.Options.Select(option => (option.Id, option.Name))],
            "checkbox" => [("false", "Not checked"), ("true", "Checked")],
            _ =>
            [
                .. table.Rows
                    .SelectMany(row => row.Cells.Where(cell => cell.PropertyId == grouped.Id && cell.Key == KnowledgeEdits.KeyOf(grouped.ValueType)).SelectMany(cell => cell.Values))
                    .Distinct()
                    .Select(id => (id, labelOf?.Invoke(id) is { Length: > 0 } title ? title : id)),
            ],
        };
        if (grouped.ValueType != "checkbox")
        {
            natural.Add((KnowledgeViewEngine.NoValue, $"No {grouped.Name}"));
        }

        var labels = natural.ToDictionary(group => group.Key, group => group.Label, StringComparer.Ordinal);
        List<string> order = [.. view.GroupOrder.Where(labels.ContainsKey).Distinct()];
        order.AddRange(natural.Select(group => group.Key).Where(key => !order.Contains(key)));
        return [.. order.Select(key => (key, labels[key]))];
    }
}
