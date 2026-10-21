using EtAlii.Adp.Designer.TableModel;
using EtAlii.Adp.Specification.Fbl.Planning;
using static EtAlii.Adp.Designer.Knowledge.KnowledgeEdits;

namespace EtAlii.Adp.Designer.Knowledge;

/// <summary>How the view shown groups its rows: by what, in which order, and which groups are hidden or collapsed.</summary>
internal static partial class KnowledgeViewEdits
{
    private const string NotGrouped = "This view is not grouped.";

    internal static KnowledgeEdit GroupBy(KnowledgeTable table, KnowledgeView view, TableGesture gesture)
    {
        if (gesture.ColumnId == view.GroupBy)
        {
            return KnowledgeEdit.Nothing;
        }

        if (gesture.ColumnId.Length > 0)
        {
            if (table.Properties.FirstOrDefault(candidate => candidate.Id == gesture.ColumnId) is not { } property)
            {
                return Refused(PropertyGone);
            }

            if (!property.Type.CanGroup)
            {
                return Refused("A view is grouped by a selection, a checkbox or a relation.");
            }
        }

        // What the view remembered of its groups was about the old grouping's groups.
        return new KnowledgeEdit([.. UngroupSettings(view), Set(view.Id, ("groupBy", gesture.ColumnId.Length > 0 ? gesture.ColumnId : null))]);
    }

    /// <summary>What a view remembers of its groups - their order, which are hidden and which collapsed - removed.</summary>
    internal static IEnumerable<ModelChange> UngroupSettings(KnowledgeView view) =>
        view.GroupOrder.Select(key => $"{view.Id}/groupOrder/{key}")
            .Concat(view.HiddenGroups.Select(key => $"{view.Id}/hiddenGroups/{key}"))
            .Concat(view.Collapsed.Select(key => $"{view.Id}/collapsed/{key}"))
            .Select(id => (ModelChange)new ModelChange.Remove(id));

    internal static KnowledgeEdit HideEmptyGroups(KnowledgeView view, TableGesture gesture) =>
        KnowledgeEdit.Of(Set(view.Id, ("hideEmptyGroups", Flag(gesture, "hide") ? true : null)));

    internal static KnowledgeEdit HideGroup(KnowledgeView view, TableGesture gesture) =>
        gesture.TargetId.Length == 0 || view.HiddenGroups.Contains(gesture.TargetId)
            ? KnowledgeEdit.Nothing
            : KnowledgeEdit.Of(new ModelChange.Add("GroupSetting", null, Attributes(("key", gesture.TargetId)), view.Id, Slot: "hiddenGroups"));

    internal static KnowledgeEdit ShowGroups(KnowledgeView view) =>
        new([.. view.HiddenGroups.Select(key => (ModelChange)new ModelChange.Remove($"{view.Id}/hiddenGroups/{key}"))]);

    internal static KnowledgeEdit ShowGroup(KnowledgeView view, TableGesture gesture) =>
        view.HiddenGroups.Contains(gesture.TargetId) ? KnowledgeEdit.Of(new ModelChange.Remove($"{view.Id}/hiddenGroups/{gesture.TargetId}")) : KnowledgeEdit.Nothing;

    /// <summary>A group, or a row with rows nested under it, collapsed or opened again.</summary>
    internal static KnowledgeEdit Collapse(KnowledgeView view, string key, bool collapsed)
    {
        if (key.Length == 0 || view.Collapsed.Contains(key) == collapsed)
        {
            return KnowledgeEdit.Nothing;
        }

        return collapsed
            ? KnowledgeEdit.Of(new ModelChange.Add("GroupSetting", null, Attributes(("key", key)), view.Id, Slot: "collapsed"))
            : KnowledgeEdit.Of(new ModelChange.Remove($"{view.Id}/collapsed/{key}"));
    }

    /// <summary>
    /// Moves a group: one place up or down among the groups that are shown, or to a place among
    /// all of them. The view's order names every group from then on: those it did not name yet are
    /// added in the order they stood in.
    /// </summary>
    internal static KnowledgeEdit MoveGroup(KnowledgeTable table, KnowledgeView view, TableGesture gesture)
    {
        if (table.Properties.FirstOrDefault(candidate => candidate.Id == view.GroupBy) is not { } grouped)
        {
            return Refused(NotGrouped);
        }

        List<string> order = [.. KnowledgeGroups.Of(table, view, grouped).Select(group => group.Key)];
        var from = order.IndexOf(gesture.TargetId);
        if (from < 0)
        {
            return KnowledgeEdit.Nothing;
        }

        int to;
        if (gesture.Settings.TryGetValue("direction", out var direction))
        {
            // Beside the next group that is shown: a hidden one is stepped over.
            List<string> shown = [.. order.Where(key => !view.HiddenGroups.Contains(key))];
            var beside = shown.IndexOf(gesture.TargetId) + (direction == "up" ? -1 : 1);
            if (beside < 0 || beside >= shown.Count)
            {
                return KnowledgeEdit.Nothing;
            }

            to = order.IndexOf(shown[beside]);
        }
        else
        {
            to = Math.Clamp(gesture.Index, 0, order.Count - 1);
        }

        List<string> desired = [.. order];
        desired.RemoveAt(from);
        desired.Insert(to, gesture.TargetId);
        return Reordered(view, order, desired);
    }

    /// <summary>Puts the groups in the order of their names, forwards or backwards; the rows without a value stay last.</summary>
    internal static KnowledgeEdit SortGroups(KnowledgeTable table, KnowledgeView view, TableGesture gesture, KnowledgeSurroundings? files)
    {
        if (table.Properties.FirstOrDefault(candidate => candidate.Id == view.GroupBy) is not { } grouped)
        {
            return Refused(NotGrouped);
        }

        var target = grouped.Type is KnowledgeRelationType ? files?.TargetOf(grouped) : null;
        var groups = KnowledgeGroups.Of(table, view, grouped, target is null ? null : id => KnowledgeRelations.LabelOf(target, id));
        var named = groups.Where(group => group.Key != KnowledgeViewEngine.NoValue).OrderBy(group => group.Label, StringComparer.CurrentCultureIgnoreCase).Select(group => group.Key);
        List<string> desired = [.. gesture.Settings.GetValueOrDefault("order") == "descending" ? named.Reverse() : named, .. groups.Where(group => group.Key == KnowledgeViewEngine.NoValue).Select(group => group.Key)];
        return Reordered(view, [.. groups.Select(group => group.Key)], desired);
    }

    /// <summary>
    /// The changes that give a view's groups an order: an entry for each group the view does not
    /// name yet, in the place it stands in now, and then one move per group that is not where it
    /// is wanted. Entries for groups that are no longer there stay behind the others.
    /// </summary>
    private static KnowledgeEdit Reordered(KnowledgeView view, List<string> order, List<string> desired)
    {
        // What the file holds, and will hold once the groups it does not name are added behind them.
        List<string> named = [.. view.GroupOrder.Distinct()];
        List<ModelChange> changes = [.. order.Where(key => !named.Contains(key)).Select(key => (ModelChange)new ModelChange.Add("GroupSetting", null, Attributes(("key", key)), view.Id, Slot: "groupOrder"))];
        List<string> work = [.. named, .. order.Where(key => !named.Contains(key))];
        List<string> wanted = [.. desired, .. work.Where(key => !desired.Contains(key))];
        for (var place = 0; place < wanted.Count; place++)
        {
            var from = work.IndexOf(wanted[place]);
            if (from == place)
            {
                continue;
            }

            changes.AddRange(MoveTo(Id(wanted[place]), view.Id, [.. work.Select(Id)], place).Changes);
            work.RemoveAt(from);
            work.Insert(place, wanted[place]);
        }

        return new KnowledgeEdit(changes);

        string Id(string key) => $"{view.Id}/groupOrder/{key}";
    }
}
