using EtAlii.Adp.Designer.TableModel;
using EtAlii.Adp.Specification.Fbl.Planning;
using static EtAlii.Adp.Designer.Knowledge.KnowledgeEdits;

namespace EtAlii.Adp.Designer.Knowledge;

/// <summary>A property that is a relation: made, given another side or a limit, or made the parent relation.</summary>
internal static partial class KnowledgeColumnEdits
{
    public const string NotARelation = "That property is not a relation of this table's own.";
    public const string ParentHoldsOne = "The parent relation holds one row.";

    /// <summary>What an answer starts with when it is a choice to make and not a refusal: the things to choose from follow, as a list of ids and names.</summary>
    private const string ChoosePrefix = "choose:";

    /// <summary>The tables a new relation can relate to, as the answer to a relation asked for without one.</summary>
    public static string ChooseTarget(IEnumerable<(string Target, string Name)> tables) =>
        ChoosePrefix + System.Text.Json.JsonSerializer.Serialize(tables.Select(table => new { id = table.Target, label = table.Name }));

    /// <summary>
    /// A relation to another knowledge file, or to this one: a property of this file, naming its target by a link to the
    /// target's document id, or by its path while the target has none (knowledge.md, section 9). Two-way, it has a backlink
    /// name: the target shows its backlinks in a column of that name, computed there and stored in neither file, so only
    /// this file is written.
    /// </summary>
    internal static KnowledgeEdit AddRelation(KnowledgeTable table, KnowledgeView? view, TableGesture gesture, Func<string> newId, KnowledgeSurroundings? files)
    {
        var targetName = (gesture.Settings.GetValueOrDefault("target") ?? "").Trim();
        if (files is null || targetName.Length == 0)
        {
            return Refused(RelationNeedsTarget);
        }

        var target = files.TargetAt(targetName);
        if (target.Table is not { } related)
        {
            return Refused(target.Problem);
        }

        var self = string.Equals(target.Path, Path.GetFullPath(files.BodyPath), StringComparison.OrdinalIgnoreCase);
        var asked = (gesture.Values.FirstOrDefault() ?? "").Trim();
        var name = UniqueName(table.Properties.Select(property => property.Name), asked.Length > 0 ? asked : related.Name.Length > 0 && !self ? related.Name : "Relation");
        // A diagram has no column to show a relation's backlinks in, so a relation to one is one-way (spec 092, FR-025).
        var otherName = target.IsDiagram ? "" : AskedOtherSide(table, gesture);
        var taken = self ? table.Properties.Select(property => property.Name).Append(name) : related.Properties.Select(property => property.Name);
        var attributes = Attributes(
            ("name", name),
            ("valueType", KnowledgeValueTypes.Relation.Name),
            ("targetFile", TargetNameOf(files.BodyPath, self ? table : related, target.Path)),
            ("limit", gesture.Settings.GetValueOrDefault("limit") == "one" ? "one" : null),
            ("backlinkName", otherName.Length > 0 ? UniqueName(taken, otherName) : null));

        return new KnowledgeEdit(Place(table, view, newId(), attributes, gesture.TargetId, gesture.Settings.GetValueOrDefault("side") == "left"));
    }

    /// <summary>
    /// How a relation of the file at <paramref name="bodyPath"/> names the knowledge file at <paramref name="targetPath"/>, whose
    /// table is <paramref name="target"/>: a link to its document id, or, while it has none, its path from here, <c>.</c> for itself.
    /// </summary>
    private static string TargetNameOf(string bodyPath, KnowledgeTable target, string targetPath) =>
        target.DocumentId.Length > 0 ? KnowledgeLinks.ToDocument(target, targetPath) : KnowledgeRelations.TargetName(bodyPath, targetPath);

    /// <summary>
    /// What the other side of a relation is to be called: the name asked for, or - for a relation
    /// asked for as two-way without a name for its other side - this table's. Empty for one-way.
    /// </summary>
    private static string AskedOtherSide(KnowledgeTable table, TableGesture gesture)
    {
        var otherName = (gesture.Settings.GetValueOrDefault("counterpart") ?? "").Trim();
        return otherName.Length == 0 && Flag(gesture, "twoWay") ? OtherSideName(table) : otherName;
    }

    /// <summary>What the other side of a two-way relation is called until its author says otherwise: this table's name.</summary>
    private static string OtherSideName(KnowledgeTable table) => table.Name.Length > 0 ? table.Name : "Relation";

    /// <summary>The relation of this table a gesture is about: one that holds its own values, not the shown side of another table's.</summary>
    private static KnowledgeProperty? OwnRelation(KnowledgeTable table, string propertyId) =>
        table.Properties.FirstOrDefault(candidate => candidate.Id == propertyId) is { Type: KnowledgeRelationType, IsComputed: false } property ? property : null;

    /// <summary>
    /// How many rows a relation's cell may hold: one, or any number. Cells that hold more than a new
    /// limit of one are kept as they are, and the table says so of each (<c>knowledge.relation-over-limit</c>).
    /// </summary>
    internal static KnowledgeEdit SetLimit(KnowledgeTable table, TableGesture gesture)
    {
        if (table.Properties.All(candidate => candidate.Id != gesture.ColumnId))
        {
            return Refused(PropertyGone);
        }

        if (OwnRelation(table, gesture.ColumnId) is not { } property)
        {
            return Refused(NotARelation);
        }

        var limit = gesture.Settings.GetValueOrDefault("limit") == "one" ? "one" : "none";
        if (limit == property.Limit)
        {
            return KnowledgeEdit.Nothing;
        }

        return property.IsParent ? Refused(ParentHoldsOne) : KnowledgeEdit.Of(Set(property.Id, ("limit", limit == "one" ? "one" : null)));
    }

    /// <summary>
    /// Whether a relation is shown in the table it relates to as well: made two-way, it is given a backlink name, the name
    /// given or this table's, under which the target shows its backlinks; made one-way, it loses it. Only this file is
    /// written. A relation of a file made before links whose other side is a counterpart property there loses that property.
    /// The values are this side's own either way, so no row changes.
    /// </summary>
    internal static KnowledgeEdit SetOtherSide(KnowledgeTable table, TableGesture gesture, KnowledgeSurroundings? files)
    {
        if (table.Properties.All(candidate => candidate.Id != gesture.ColumnId))
        {
            return Refused(PropertyGone);
        }

        if (OwnRelation(table, gesture.ColumnId) is not { } property)
        {
            return Refused(NotARelation);
        }

        var self = property.TargetsItsOwnTable;
        var target = self ? null : files?.TargetOf(property);
        if (!self && target?.Table is null)
        {
            return Refused(target?.Problem is { Length: > 0 } problem ? problem : RelationNeedsTarget);
        }

        var related = self ? table : target!.Table!;
        var other = related.Properties.FirstOrDefault(candidate => candidate.Id == property.Counterpart && property.Counterpart.Length > 0);
        var twoWay = Flag(gesture, "twoWay");
        if (twoWay == (other is not null || property.BacklinkName.Length > 0))
        {
            return KnowledgeEdit.Nothing;
        }

        if (!twoWay && other is not null)
        {
            // A counterpart property of a file made before links goes with the side that named it.
            ModelChange mine = Set(property.Id, ("counterpart", null));
            ModelChange theirs = new ModelChange.Remove(other.Id);
            return self ? new KnowledgeEdit([mine, theirs]) : new KnowledgeEdit([mine], Others: [new KnowledgeOtherFile(target!.Path, [theirs])]);
        }

        if (!twoWay)
        {
            return KnowledgeEdit.Of(Set(property.Id, ("backlinkName", null)));
        }

        var asked = (gesture.Settings.GetValueOrDefault("counterpart") ?? "").Trim();
        return KnowledgeEdit.Of(Set(property.Id, ("backlinkName", UniqueName(related.Properties.Select(candidate => candidate.Name), asked.Length > 0 ? asked : OtherSideName(table)))));
    }

    /// <summary>
    /// Makes a relation the parent relation, or no longer that: the one a view nests its rows by.
    /// Only a relation to the table itself that holds one row can be it, and a table has one.
    /// </summary>
    internal static KnowledgeEdit SetParent(KnowledgeTable table, TableGesture gesture)
    {
        if (table.Properties.FirstOrDefault(candidate => candidate.Id == gesture.ColumnId) is not { } property)
        {
            return Refused(PropertyGone);
        }

        var parent = Flag(gesture, "parent");
        if (parent == property.IsParent)
        {
            return KnowledgeEdit.Nothing;
        }

        if (!parent)
        {
            return KnowledgeEdit.Of(Set(property.Id, ("isParent", null)));
        }

        if (property is not { Type: KnowledgeRelationType, TargetsItsOwnTable: true, Limit: "one", IsComputed: false })
        {
            return Refused("Only a relation to this table that holds one row can be the parent relation.");
        }

        if (table.Properties.FirstOrDefault(other => other.IsParent) is { } already)
        {
            return Refused($"'{already.Name}' is the parent relation already.");
        }

        // What is there must not be a circle already: a parent relation is never one.
        return table.Rows.Any(row => row.Cells.FirstOrDefault(cell => cell.PropertyId == property.Id && cell.Key == "rows")?.Values.FirstOrDefault() is { } above
                                     && KnowledgeRelations.WouldCycle(table, property, row.Id, above))
            ? Refused("Some rows of this relation are each other's ancestors, so it cannot be the parent relation.")
            : KnowledgeEdit.Of(Set(property.Id, ("isParent", true)));
    }
}
