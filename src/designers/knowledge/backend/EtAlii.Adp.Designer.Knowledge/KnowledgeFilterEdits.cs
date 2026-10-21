using System.Globalization;
using EtAlii.Adp.Designer.TableModel;
using EtAlii.Adp.Specification.Fbl.Planning;
using static EtAlii.Adp.Designer.Knowledge.KnowledgeEdits;

namespace EtAlii.Adp.Designer.Knowledge;

/// <summary>
/// The gestures that change the filter of the view shown: its conditions and its groups of them,
/// each item named by its path - places joined by a slash, empty for the view's own group.
/// </summary>
internal static class KnowledgeFilterEdits
{
    /// <summary>How deep a filter's groups go: the bindings read a group inside a group and no further.</summary>
    private const int FilterDepth = 2;

    /// <summary>A filter's item and its id, by its path: empty for the view's own group, else places joined by a slash.</summary>
    private static (string Id, KnowledgeFilterItem Item, int Depth)? FilterItem(KnowledgeView view, string path)
    {
        KnowledgeFilterItem item = view.Filter;
        var id = $"{view.Id}/filter";
        var depth = 0;
        foreach (var segment in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (item is not KnowledgeFilterGroup group || !int.TryParse(segment, NumberStyles.None, CultureInfo.InvariantCulture, out var index) || index >= group.Items.Count)
            {
                return null;
            }

            item = group.Items[index];
            id = FormattableString.Invariant($"{id}/{index}");
            depth++;
        }

        return (depth == 0 ? view.Id : id, item, depth);
    }

    /// <summary>
    /// A condition or a group added as the last item of a group, in every format. A format that adds
    /// an entry after the last one its rule reads would put a condition before the groups that follow
    /// the conditions; there the item is moved to the end in the same step, so a filter reads the same
    /// whichever format the file has.
    /// </summary>
    private static KnowledgeEdit AddToFilter(string type, Dictionary<string, object?> attributes, KnowledgeView view, (string Id, KnowledgeFilterItem Item, int Depth) group, bool besideItsRule)
    {
        var items = ((KnowledgeFilterGroup)group.Item).Items;
        var add = new ModelChange.Add(type, null, attributes, group.Id);
        if (!besideItsRule)
        {
            return KnowledgeEdit.Of(add);
        }

        var last = -1;
        for (var index = 0; index < items.Count; index++)
        {
            if (items[index] is KnowledgeCondition == (type == "Condition"))
            {
                last = index;
            }
        }

        if (last < 0 || last == items.Count - 1)
        {
            return KnowledgeEdit.Of(add);
        }

        var prefix = group.Depth == 0 ? $"{view.Id}/filter" : group.Id;
        return KnowledgeEdit.Of(add, new ModelChange.Move(FormattableString.Invariant($"{prefix}/{last + 1}"), group.Id, items.Count + 1));
    }

    internal static KnowledgeEdit AddFilter(KnowledgeTable table, KnowledgeView view, TableGesture gesture, bool besideItsRule)
    {
        if (table.Properties.FirstOrDefault(candidate => candidate.Id == gesture.ColumnId) is not { } property)
        {
            return Refused(PropertyGone);
        }

        if (FilterItem(view, gesture.TargetId) is not { Item: KnowledgeFilterGroup } group)
        {
            return Refused("That group is no longer in this filter.");
        }

        var comparison = gesture.Settings.GetValueOrDefault("comparison") is { Length: > 0 } asked ? Comparison(asked) : property.Type.Comparisons.FirstOrDefault() ?? "is-empty";
        return AddToFilter("Condition", Attributes(("property", property.Id), ("operator", comparison)), view, group, besideItsRule);
    }

    internal static KnowledgeEdit AddFilterGroup(KnowledgeView view, TableGesture gesture, bool besideItsRule)
    {
        if (FilterItem(view, gesture.TargetId) is not { Item: KnowledgeFilterGroup } group)
        {
            return Refused("That group is no longer in this filter.");
        }

        return group.Depth >= FilterDepth
            ? Refused("A filter's groups go two deep.")
            : AddToFilter("FilterGroup", Attributes(("match", "all")), view, group, besideItsRule);
    }

    internal static KnowledgeEdit SetFilter(KnowledgeTable table, KnowledgeView view, TableGesture gesture)
    {
        if (FilterItem(view, gesture.TargetId) is not { Item: KnowledgeCondition condition } item)
        {
            return Refused("That condition is no longer in this filter.");
        }

        var propertyId = gesture.ColumnId.Length > 0 ? gesture.ColumnId : condition.PropertyId;
        if (table.Properties.All(property => property.Id != propertyId))
        {
            return Refused(PropertyGone);
        }

        // One value under the key its property's type writes it under, and no other value left beside it.
        var attributes = Attributes(
        [
            ("property", propertyId),
            ("operator", gesture.Settings.GetValueOrDefault("comparison") is { Length: > 0 } comparison ? Comparison(comparison) : condition.Operator),
            .. KnowledgeVocabulary.ValueKeys.Select(key => (key, (object?)null)),
        ]);
        var value = gesture.Values.FirstOrDefault() ?? "";
        if (value.Length > 0)
        {
            if (ConditionValue(table, propertyId, value, out var key, out var written) is { Length: > 0 } refusal)
            {
                return Refused(refusal);
            }

            attributes[key] = written;
        }

        return KnowledgeEdit.Of(new ModelChange.Set(item.Id, attributes));
    }

    internal static KnowledgeEdit SetFilterMatch(KnowledgeView view, TableGesture gesture)
    {
        if (FilterItem(view, gesture.TargetId) is not { Item: KnowledgeFilterGroup } group)
        {
            return Refused("That group is no longer in this filter.");
        }

        var any = gesture.Settings.GetValueOrDefault("match") == "any";
        return group.Depth == 0
            ? KnowledgeEdit.Of(Set(view.Id, ("filterMatch", any ? "any" : null)))
            : KnowledgeEdit.Of(Set(group.Id, ("match", any ? "any" : "all")));
    }

    internal static KnowledgeEdit RemoveFilter(KnowledgeView view, TableGesture gesture) =>
        FilterItem(view, gesture.TargetId) is { } item && gesture.TargetId.Length > 0 ? KnowledgeEdit.Of(new ModelChange.Remove(item.Id)) : KnowledgeEdit.Nothing;

    /// <summary>A filter's items under a new parent, each addressed by its place as the reading will address it.</summary>
    internal static void CopyFilter(KnowledgeTable table, KnowledgeFilterGroup group, string parentId, string prefix, List<ModelChange> changes)
    {
        for (var index = 0; index < group.Items.Count; index++)
        {
            var itemId = FormattableString.Invariant($"{prefix}/{index}");
            switch (group.Items[index])
            {
                case KnowledgeFilterGroup nested:
                    changes.Add(new ModelChange.Add("FilterGroup", null, Attributes(("match", nested.Any ? "any" : "all")), parentId));
                    CopyFilter(table, nested, itemId, itemId, changes);
                    break;
                case KnowledgeCondition condition:
                    var attributes = Attributes(("property", condition.PropertyId), ("operator", condition.Operator));
                    if (condition.Value.Length > 0 && ConditionValue(table, condition.PropertyId, condition.Value, out var key, out var written).Length == 0 && written is not null)
                    {
                        attributes[key] = written;
                    }

                    changes.Add(new ModelChange.Add("Condition", null, attributes, parentId));
                    break;
            }
        }
    }

    /// <summary>What a condition is compared with, as its property's type writes it. Several values are compared one at a time.</summary>
    private static string ConditionValue(KnowledgeTable table, string propertyId, string value, out string key, out object? written) =>
        (table.Properties.FirstOrDefault(property => property.Id == propertyId)?.Type ?? KnowledgeValueTypes.Text).WriteCondition(value, out key, out written);

    /// <summary>
    /// A comparison by the name the file knows it by. The two every type has - is empty, is not
    /// empty - are the table library's own and arrive under its names for them.
    /// </summary>
    private static string Comparison(string asked) => asked switch
    {
        "isEmpty" => "is-empty",
        "isNotEmpty" => "is-not-empty",
        _ => asked,
    };
}
