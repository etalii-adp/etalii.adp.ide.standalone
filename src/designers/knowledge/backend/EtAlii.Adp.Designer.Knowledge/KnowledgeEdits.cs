using EtAlii.Adp.Designer.TableModel;
using EtAlii.Adp.Specification.Fbl.Planning;

namespace EtAlii.Adp.Designer.Knowledge;

/// <summary>What a gesture comes to: the changes to make to the file, in order, or why it is refused.</summary>
/// <param name="Changes">The changes, one step together. Empty for a gesture that changes nothing.</param>
/// <param name="Refusal">Why the gesture is refused, as a sentence for the author; empty when it is not.</param>
/// <param name="NewRowId">The id of the row the gesture adds, or empty.</param>
/// <param name="NewViewId">The id of the view the gesture adds, or empty.</param>
/// <param name="Others">What the same step changes in other files: the other side of a two-way relation.</param>
internal sealed record KnowledgeEdit(IReadOnlyList<ModelChange> Changes, string Refusal = "", string NewRowId = "", string NewViewId = "", IReadOnlyList<KnowledgeOtherFile>? Others = null)
{
    public IReadOnlyList<KnowledgeOtherFile> Others { get; } = Others ?? [];

    public bool IsRefused => Refusal.Length > 0;

    public static KnowledgeEdit Refused(string reason) => new([], reason);

    public static KnowledgeEdit Nothing { get; } = new([]);

    public static KnowledgeEdit Of(params ModelChange[] changes) => new(changes);
}

/// <summary>What an edit may know of the files around the one it edits: where that file is, and what its relations point at.</summary>
/// <param name="BodyPath">The file being edited.</param>
/// <param name="TargetOf">What a relation property of the file points at.</param>
/// <param name="TargetAt">What a target named from the file - a relative path, or <c>.</c> for the file itself - is.</param>
internal sealed record KnowledgeSurroundings(string BodyPath, Func<KnowledgeProperty, KnowledgeTarget?> TargetOf, Func<string, KnowledgeTarget> TargetAt);

/// <summary>
/// The table's gestures as changes of the file: one place that knows what each gesture means for
/// a knowledge file, what it refuses and in which words. It reads a table and writes nothing; the
/// changes it gives are made to the file through the FBL runtime and to what the table shows
/// through <see cref="KnowledgeProjection"/>, so the two cannot mean different things.
/// </summary>
/// <remarks>
/// Each gesture is an entry of <see cref="Gestures"/>, found by its kind, and planned by the edits
/// of what it changes: <see cref="KnowledgeCellEdits"/> for a row's cells and the rows,
/// <see cref="KnowledgeColumnEdits"/> for a property and what it has, <see cref="KnowledgeViewEdits"/>
/// for a view and how it shows the rows, and <see cref="KnowledgeFilterEdits"/> for a view's filter.
/// What they share is here.
/// </remarks>
internal static class KnowledgeEdits
{
    internal const string PropertyGone = "That property is no longer in this table.";
    internal const string ViewGone = "That view is no longer in this table.";
    public const string TitleStays = "The title property is always there: it cannot be deleted or hidden.";

    /// <summary>The id the root of a knowledge file has: its rule stores none, and the designer derives this one.</summary>
    internal const string TableId = "table";

    /// <summary>
    /// Every gesture of the table, each with whether it is about the view shown. Which gesture a kind
    /// is does not depend on where it stands here.
    /// </summary>
    internal static IReadOnlyList<KnowledgeGesture> Gestures { get; } =
    [
        // A row's cells, and the rows.
        new("setCell", RequiresView: false, request => KnowledgeCellEdits.SetCell(request.Table, request.Gesture, request.NewId, request.Files)),
        new("setCells", RequiresView: false, request => KnowledgeCellEdits.SetCells(request.Table, request.Gesture, request.NewId, request.Files)),
        new("addRow", RequiresView: false, request => KnowledgeCellEdits.AddRow(request.Table, request.View, request.Gesture, request.NewId)),
        new("deleteRows", RequiresView: false, request => KnowledgeCellEdits.DeleteRows(request.Table, request.Gesture)),
        new("moveRows", RequiresView: true, request => KnowledgeCellEdits.MoveRows(request.Table, request.RequiredView, request.Gesture)),

        // A property, its type, its relation and its options.
        new("addColumn", RequiresView: false, request => KnowledgeColumnEdits.AddColumn(request.Table, request.View, request.Gesture, request.NewId)),
        new("renameColumn", RequiresView: false, request => KnowledgeColumnEdits.RenameColumn(request.Table, request.Gesture)),
        new("duplicateColumn", RequiresView: false, request => KnowledgeColumnEdits.DuplicateColumn(request.Table, request.View, request.Gesture, request.NewId)),
        new("deleteColumn", RequiresView: false, request => KnowledgeColumnEdits.DeleteColumn(request.Table, request.Gesture, request.Files)),
        new("setColumnType", RequiresView: false, request => KnowledgeColumnEdits.ChangeType(request.Table, request.Gesture, request.NewId)),
        new("addRelation", RequiresView: false, request => KnowledgeColumnEdits.AddRelation(request.Table, request.View, request.Gesture, request.NewId, request.Files)),
        new("setColumnParent", RequiresView: false, request => KnowledgeColumnEdits.SetParent(request.Table, request.Gesture)),
        new("setRelationLimit", RequiresView: false, request => KnowledgeColumnEdits.SetLimit(request.Table, request.Gesture)),
        new("setRelationOtherSide", RequiresView: false, request => KnowledgeColumnEdits.SetOtherSide(request.Table, request.Gesture, request.NewId, request.Files)),
        new("addOption", RequiresView: false, request => KnowledgeColumnEdits.AddOption(request.Table, request.Gesture, request.NewId)),
        new("renameOption", RequiresView: false, request => KnowledgeColumnEdits.RenameOption(request.Table, request.Gesture)),
        new("recolourOption", RequiresView: false, request => KnowledgeColumnEdits.RecolourOption(request.Table, request.Gesture)),
        new("moveOption", RequiresView: false, request => KnowledgeColumnEdits.MoveOption(request.Table, request.Gesture)),
        new("deleteOption", RequiresView: false, request => KnowledgeColumnEdits.DeleteOption(request.Table, request.Gesture)),

        // The views, each named by the gesture itself.
        new("addView", RequiresView: false, request => KnowledgeViewEdits.AddView(request.Table, request.NewId)),
        new("renameView", RequiresView: false, request => KnowledgeViewEdits.RenameView(request.Table, request.Gesture)),
        new("duplicateView", RequiresView: false, request => KnowledgeViewEdits.DuplicateView(request.Table, request.Gesture, request.NewId)),
        new("deleteView", RequiresView: false, request => KnowledgeViewEdits.DeleteView(request.Table, request.Gesture)),
        new("moveView", RequiresView: false, request => KnowledgeViewEdits.MoveView(request.Table, request.Gesture)),

        // The view shown: its columns.
        new("moveColumn", RequiresView: true, request => KnowledgeViewEdits.MoveColumn(request.Table, request.RequiredView, request.Gesture)),
        new("resizeColumn", RequiresView: true, request => KnowledgeViewEdits.ResizeColumn(request.Table, request.RequiredView, request.Gesture)),
        new("hideColumn", RequiresView: true, request => KnowledgeViewEdits.ShowColumn(request.Table, request.RequiredView, request.Gesture, visible: false)),
        new("showColumn", RequiresView: true, request => KnowledgeViewEdits.ShowColumn(request.Table, request.RequiredView, request.Gesture, visible: true)),
        new("setColumnWrap", RequiresView: true, request => KnowledgeViewEdits.WrapColumn(request.Table, request.RequiredView, request.Gesture)),
        new("setColumnCalculation", RequiresView: true, request => KnowledgeViewEdits.Calculate(request.Table, request.RequiredView, request.Gesture)),
        new("freezeColumns", RequiresView: true, request => KnowledgeViewEdits.Freeze(request.Table, request.RequiredView, request.Gesture)),

        // The view shown: its sorts.
        new("addSort", RequiresView: true, request => KnowledgeViewEdits.AddSort(request.Table, request.RequiredView, request.Gesture)),
        new("setSort", RequiresView: true, request => KnowledgeViewEdits.SetSort(request.RequiredView, request.Gesture)),
        new("removeSort", RequiresView: true, request => KnowledgeViewEdits.RemoveSort(request.RequiredView, request.Gesture)),
        new("moveSort", RequiresView: true, request => KnowledgeViewEdits.MoveSort(request.RequiredView, request.Gesture)),

        // The view shown: its groups.
        new("groupBy", RequiresView: true, request => KnowledgeViewEdits.GroupBy(request.Table, request.RequiredView, request.Gesture)),
        new("setHideEmptyGroups", RequiresView: true, request => KnowledgeViewEdits.HideEmptyGroups(request.RequiredView, request.Gesture)),
        new("toggleGroup", RequiresView: true, request => KnowledgeViewEdits.Collapse(request.RequiredView, request.Gesture.TargetId, Flag(request.Gesture, "collapsed"))),
        new("toggleRow", RequiresView: true, request => KnowledgeViewEdits.Collapse(request.RequiredView, request.Gesture.RowId, Flag(request.Gesture, "collapsed"))),
        new("hideGroup", RequiresView: true, request => KnowledgeViewEdits.HideGroup(request.RequiredView, request.Gesture)),
        new("showGroups", RequiresView: true, request => KnowledgeViewEdits.ShowGroups(request.RequiredView)),
        new("showGroup", RequiresView: true, request => KnowledgeViewEdits.ShowGroup(request.RequiredView, request.Gesture)),
        new("moveGroup", RequiresView: true, request => KnowledgeViewEdits.MoveGroup(request.Table, request.RequiredView, request.Gesture)),
        new("sortGroups", RequiresView: true, request => KnowledgeViewEdits.SortGroups(request.Table, request.RequiredView, request.Gesture, request.Files)),

        // The view shown: its filter.
        new("addFilter", RequiresView: true, request => KnowledgeFilterEdits.AddFilter(request.Table, request.RequiredView, request.Gesture, request.BesideItsRule)),
        new("addFilterGroup", RequiresView: true, request => KnowledgeFilterEdits.AddFilterGroup(request.RequiredView, request.Gesture, request.BesideItsRule)),
        new("setFilter", RequiresView: true, request => KnowledgeFilterEdits.SetFilter(request.Table, request.RequiredView, request.Gesture)),
        new("setFilterMatch", RequiresView: true, request => KnowledgeFilterEdits.SetFilterMatch(request.RequiredView, request.Gesture)),
        new("removeFilter", RequiresView: true, request => KnowledgeFilterEdits.RemoveFilter(request.RequiredView, request.Gesture)),
    ];

    private static readonly KnowledgeGestureTable _gestures = new(Gestures);

    /// <summary>
    /// A kind the table does not know. It is refused for want of a view as well, before it is refused
    /// as unknown: a table without views answers every gesture about a view it lacks that way.
    /// </summary>
    private static readonly KnowledgeGesture _unknown = new("", RequiresView: true, _ => Refused("This table does not know how to do that."));

    /// <param name="table">The table as it is shown now, edits not yet written included.</param>
    /// <param name="activeViewId">The view the connection shows, for a gesture that names none.</param>
    /// <param name="gesture">What the author did.</param>
    /// <param name="newId">Gives an id that was never used: called once for everything the gesture makes that stores an id.</param>
    /// <param name="besideItsRule">Whether the file's format adds an entry after the last one its rule reads (<see cref="KnowledgeDefinition.AddsBesideItsRule"/>).</param>
    /// <param name="files">The files around this one, for a gesture about a relation; null where there are none to know of.</param>
    public static KnowledgeEdit Plan(KnowledgeTable table, string activeViewId, TableGesture gesture, Func<string> newId, bool besideItsRule = false, KnowledgeSurroundings? files = null) =>
        Plan(_gestures, table, activeViewId, gesture, newId, besideItsRule, files);

    /// <summary>A gesture planned with the gestures of <paramref name="gestures"/>: the table's own, or the same listed in another order.</summary>
    internal static KnowledgeEdit Plan(KnowledgeGestureTable gestures, KnowledgeTable table, string activeViewId, TableGesture gesture, Func<string> newId, bool besideItsRule, KnowledgeSurroundings? files)
    {
        ArgumentNullException.ThrowIfNull(gestures);
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(gesture);
        ArgumentNullException.ThrowIfNull(newId);

        var view = table.Views.FirstOrDefault(candidate => candidate.Id == (gesture.ViewId.Length > 0 ? gesture.ViewId : activeViewId)) ?? table.ViewOrDefault(activeViewId);
        var planned = gestures.Of(gesture.Kind, _unknown);
        return planned.RequiresView && view is null
            ? Refused(ViewGone)
            : planned.Plan(new KnowledgeGestureRequest(table, view, gesture, newId, besideItsRule, files));
    }

    // ---- what the edits share ----

    /// <summary>
    /// The colour a new option starts with, as Notion gives one: any of the palette's colours, and
    /// one the property's options do not have yet while there is one. Which one follows from the
    /// option's name, so the same option made in two files, or made again after an undo, looks the same.
    /// </summary>
    internal static string ColourFor(KnowledgeProperty property, string name)
    {
        List<string> palette = [.. KnowledgeVocabulary.Colours.Where(colour => colour != "default")];
        List<string> free = [.. palette.Where(colour => property.Options.All(option => option.Colour != colour))];
        var pool = free.Count > 0 ? free : palette;
        var sum = name.ToUpperInvariant().Aggregate(17, (total, letter) => unchecked((total * 31) + letter));
        return pool[(int)((uint)sum % (uint)pool.Count)];
    }

    /// <summary>
    /// Something added at a place among its siblings. A declared binding adds at the end and nowhere
    /// else, so a place is two changes of the one step: added last, then moved to where it belongs.
    /// </summary>
    /// <param name="type">The type of what is added.</param>
    /// <param name="id">The id it stores, or null for one it does not.</param>
    /// <param name="attributes">Its attributes.</param>
    /// <param name="parentId">What it is added to, or null for the table itself.</param>
    /// <param name="index">Where it is to stand, or null for the end.</param>
    /// <param name="count">How many siblings there are before it is added.</param>
    /// <param name="addedId">The id the added element is read under, stored or derived.</param>
    internal static IEnumerable<ModelChange> AddAt(string type, string? id, Dictionary<string, object?> attributes, string? parentId, int? index, int count, string addedId)
    {
        yield return new ModelChange.Add(type, id, attributes, parentId);
        if (index is { } at && at < count)
        {
            yield return new ModelChange.Move(addedId, parentId, at);
        }
    }

    internal static string CellId(string rowId, string propertyId) => $"{rowId}/{propertyId}";

    internal static KnowledgeEdit Refused(string reason) => KnowledgeEdit.Refused(reason);

    internal static bool Flag(TableGesture gesture, string name) => gesture.Settings.GetValueOrDefault(name) == "true";

    internal static ModelChange.Set Set(string id, params (string Name, object? Value)[] attributes) => new(id, Attributes(attributes));

    internal static Dictionary<string, object?> Attributes(params (string Name, object? Value)[] attributes)
    {
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach ((string name, object? value) in attributes)
        {
            result[name] = value;
        }

        return result;
    }

    internal static int IndexOf<T>(IReadOnlyList<T> items, Func<T, bool> matches)
    {
        for (var index = 0; index < items.Count; index++)
        {
            if (matches(items[index]))
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>
    /// Moves one of <paramref name="siblings"/> so that it ends at <paramref name="to"/> among them.
    /// The runtime's index counts the siblings as they are before the move, the moved one included,
    /// so a place further on is one more than where it ends.
    /// </summary>
    internal static KnowledgeEdit MoveTo(string id, string? parentId, IReadOnlyList<string> siblings, int to)
    {
        var from = IndexOf(siblings, sibling => sibling == id);
        var target = Math.Clamp(to, 0, siblings.Count - 1);
        return from < 0 || from == target ? KnowledgeEdit.Nothing : KnowledgeEdit.Of(new ModelChange.Move(id, parentId, target > from ? target + 1 : target));
    }

    /// <summary><paramref name="name"/>, or it with the lowest number that no other of <paramref name="taken"/> has.</summary>
    internal static string UniqueName(IEnumerable<string> taken, string name)
    {
        var names = taken.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!names.Contains(name))
        {
            return name;
        }

        for (var number = 2; ; number++)
        {
            var candidate = FormattableString.Invariant($"{name} {number}");
            if (!names.Contains(candidate))
            {
                return candidate;
            }
        }
    }
}
