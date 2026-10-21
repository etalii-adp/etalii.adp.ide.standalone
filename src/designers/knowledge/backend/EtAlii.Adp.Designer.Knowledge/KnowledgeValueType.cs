namespace EtAlii.Adp.Designer.Knowledge;

/// <summary>
/// One value type a property can have, and everything that differs by it: the key a value is
/// stored under, how a filter compares it and a sort orders it, which groups a grouping by it has,
/// how it is written, and what a value of it becomes when the property changes type. One strategy
/// per type, found by name in <see cref="KnowledgeValueTypes"/>, so that what a type means is said
/// in one place rather than in a switch at every site that asks.
/// </summary>
/// <remarks>
/// <b>What this class does itself is what a type without a behaviour of its own does</b>: a value
/// is compared, sorted, shown and grouped as the text it is written as. Text and a type the module
/// does not know both behave so, which is why the defaults are text's.
/// </remarks>
internal abstract class KnowledgeValueType
{
    /// <summary>What a value of a type that cannot be set from a single written value is refused with.</summary>
    public const string CannotBeSet = "This value cannot be set here.";

    private protected KnowledgeValueType(string name, string storedKey, IReadOnlyList<string> comparisons)
    {
        Name = name;
        StoredKey = storedKey;
        Comparisons = comparisons;
    }

    /// <summary>The type's name, as a property's <c>valueType</c> holds it.</summary>
    public string Name { get; }

    /// <summary>
    /// The key a value of this type is stored under: an attribute of the cell for a type that holds
    /// one value, and of each of the cell's items for a type that holds several.
    /// </summary>
    public string StoredKey { get; }

    /// <summary>The key a cell holding a value of this type has: the stored key, or the name of the list of items for a type that holds several.</summary>
    public virtual string CellKey => StoredKey;

    /// <summary>Whether a cell holds several values of this type, as items, rather than one.</summary>
    public virtual bool HoldsSeveral => false;

    /// <summary>Whether the type is one the designer's specification has.</summary>
    public virtual bool IsKnown => true;

    /// <summary>Whether a value of this type is one of the property's options.</summary>
    public virtual bool HoldsOptions => false;

    /// <summary>Whether a view can be grouped by a property of this type.</summary>
    public virtual bool CanGroup => false;

    /// <summary>What the line under a column of this type can figure besides the counts.</summary>
    public virtual KnowledgeFigures Figures => KnowledgeFigures.Counts;

    /// <summary>
    /// The comparisons a filter has for this type, in the order they are offered - the first is
    /// what a new condition starts as - besides <i>is empty</i> and <i>is not empty</i>, which every type has.
    /// </summary>
    public IReadOnlyList<string> Comparisons { get; }

    /// <summary>Whether a filter can compare a value of this type in this way.</summary>
    public bool Compares(string comparison) => comparison is "is-empty" or "is-not-empty" || Comparisons.Contains(comparison);

    // ---- values ----

    /// <summary>Whether a value held counts as a value: a box that is not ticked holds nothing, as a cell without a value does.</summary>
    public virtual bool IsValue(string value) => true;

    /// <summary>A value of this type as a point to compare, or null for a type that has none or a value that is not one.</summary>
    public virtual long? Moment(string value) => null;

    /// <summary>How long it is between two of this type's moments.</summary>
    public virtual TimeSpan Between(long earliest, long latest) => TimeSpan.FromTicks(latest - earliest);

    /// <summary>A value as its cell shows it, or null for a type whose cell shows no text and which a search never finds.</summary>
    /// <param name="property">The property the value is of.</param>
    /// <param name="value">The value as written.</param>
    /// <param name="labelOf">What a related row is called, by the relation's property and the row's id; null where relations are not resolved.</param>
    public virtual string? Shown(KnowledgeProperty property, string value, Func<string, string, string>? labelOf) => value;

    // ---- comparisons ----

    /// <summary>
    /// Whether a row's values pass a comparison this type has (<see cref="Compares"/>), other than
    /// <i>is empty</i> and <i>is not empty</i>. A comparison that needs a value and is given none
    /// is not finished, and lets every row through.
    /// </summary>
    public virtual bool Holds(string comparison, IReadOnlyList<string> values, string given) =>
        given.Length == 0 || HoldsGiven(comparison, values, given);

    /// <summary>Whether a row's values pass a comparison with a given value: as text, without regard to case.</summary>
    protected virtual bool HoldsGiven(string comparison, IReadOnlyList<string> values, string given)
    {
        var value = First(values);
        return comparison switch
        {
            "is" => string.Equals(value, given, StringComparison.OrdinalIgnoreCase),
            "is-not" => !string.Equals(value, given, StringComparison.OrdinalIgnoreCase),
            "contains" => value.Contains(given, StringComparison.OrdinalIgnoreCase),
            "does-not-contain" => !value.Contains(given, StringComparison.OrdinalIgnoreCase),
            "starts-with" => value.StartsWith(given, StringComparison.OrdinalIgnoreCase),
            "ends-with" => value.EndsWith(given, StringComparison.OrdinalIgnoreCase),
            _ => true,
        };
    }

    /// <summary>Whether one of several values is the given one: how a type that holds several compares.</summary>
    protected static bool HoldsAmong(string comparison, IReadOnlyList<string> values, string given) => comparison switch
    {
        "contains" => values.Contains(given),
        "does-not-contain" => !values.Contains(given),
        _ => true,
    };

    /// <summary>The first of a row's values, or empty when it has none.</summary>
    protected static string First(IReadOnlyList<string> values) => values.Count > 0 ? values[0] : "";

    // ---- sorting ----

    /// <summary>What a row with this value is sorted by: as text, unless the type says otherwise.</summary>
    public virtual KnowledgeSortKey SortKeyOf(KnowledgeProperty property, string value) => new(0, value);

    // ---- grouping ----

    /// <summary>
    /// The groups a grouping by this type has whatever the rows hold, in their natural order; null
    /// for a type whose groups are the values the rows hold, each as they are first met.
    /// </summary>
    public virtual IEnumerable<(string Key, string Label)>? NamedGroups(KnowledgeProperty grouped) => null;

    /// <summary>Whether a grouping by this type has a group for the rows without a value.</summary>
    public virtual bool HasNoValueGroup => true;

    /// <summary>The groups a row with these values is under: each of its values.</summary>
    public virtual IEnumerable<string> GroupsOf(IReadOnlyList<string> values) => values.Distinct();

    /// <summary>Whether a row added in this group is given the value that puts it there.</summary>
    public virtual bool TakesGroup(KnowledgeProperty grouped, string group) => false;

    // ---- writing ----

    /// <summary>
    /// A value as this type writes it, under <see cref="StoredKey"/>: what is stored - null for
    /// nothing - or why it is not a value of this type.
    /// </summary>
    /// <returns>Why the value is refused, or empty when it is not.</returns>
    public virtual string Write(string value, out object? written)
    {
        written = value;
        return CannotBeSet;
    }

    /// <summary>What a filter's condition on this type is compared with, and the key it is written under.</summary>
    /// <returns>Why the value is refused, or empty when it is not.</returns>
    public virtual string WriteCondition(string value, out string key, out object? written)
    {
        key = StoredKey;
        return Write(value, out written);
    }

    // ---- a change of type ----

    /// <summary>
    /// Converts one cell's value of this type to the type the property changes to, as the
    /// conversion table of the designer's specification says - or leaves it, under the key it has,
    /// where the value has no meaning in that type.
    /// </summary>
    public virtual void ConvertTo(KnowledgeValueType to, KnowledgeConversion conversion)
    {
    }

    /// <summary>Converts a text, trimmed, to this type, or leaves it where it has no meaning in it.</summary>
    public virtual void FromText(string value, KnowledgeConversion conversion)
    {
    }

    /// <summary>A value that becomes text when the property does: the value as it is written.</summary>
    protected static void ToText(KnowledgeValueType to, KnowledgeConversion conversion)
    {
        if (to is KnowledgeTextType)
        {
            conversion.Replace((to.StoredKey, conversion.Value));
        }
    }

    /// <summary>A text this type writes as one of its values, under its key; one it refuses stays as it is.</summary>
    protected void FromWritten(string value, KnowledgeConversion conversion)
    {
        if (Write(value, out var written).Length == 0)
        {
            conversion.Replace((StoredKey, written));
        }
    }

    public override string ToString() => Name;
}

/// <summary>What the line under a column can figure besides the counts: nothing, the numbers' figures, or the ends of the dates.</summary>
internal enum KnowledgeFigures
{
    Counts,
    Numbers,
    Dates,
}

/// <summary>What a row is sorted by for one property: a number, or a text, or nothing.</summary>
internal readonly record struct KnowledgeSortKey(double Number, string Text, bool HasValue = true)
{
    public static KnowledgeSortKey Empty { get; } = new(0, "", HasValue: false);

    public static int Compare(KnowledgeSortKey left, KnowledgeSortKey right, bool descending)
    {
        if (!left.HasValue || !right.HasValue)
        {
            // Without a value is last, in either direction.
            return left.HasValue == right.HasValue ? 0 : left.HasValue ? -1 : 1;
        }

        var order = left.Number.CompareTo(right.Number);
        if (order == 0)
        {
            order = string.Compare(left.Text, right.Text, StringComparison.OrdinalIgnoreCase);
        }

        return descending ? -order : order;
    }
}
