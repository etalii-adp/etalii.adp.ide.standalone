namespace EtAlii.Adp.Designer.Knowledge;

/// <summary>
/// The words of the designer's specification that this module implements, written out: the value
/// types, the key each is stored under, the comparisons each can be filtered by, and what a view
/// stores. The code that reads, edits and arranges a table goes by these.
/// </summary>
/// <remarks>
/// <b>Written out rather than read from <c>knowledge.des</c>, on purpose.</b> A module that read
/// its vocabulary from the specification would agree with it by construction and implement none
/// of what was added to it. Written here, the two can differ - and a test fails when they do, in
/// either direction: a type the specification has and this module does not, or one this module
/// has that the specification never heard of (knowledge-designer Requirement 10.5).
/// </remarks>
internal static class KnowledgeVocabulary
{
    /// <summary>The value types a property can have.</summary>
    public static IReadOnlyList<string> ValueTypes { get; } = ["text", "number", "checkbox", "date", "dateTime", "time", "selection", "multipleSelection", "relation"];

    /// <summary>The keys a cell or a condition holds its one value under.</summary>
    public static IReadOnlyList<string> ValueKeys { get; } = ["text", "number", "checked", "date", "dateTime", "time", "option"];

    /// <summary>The colours an option can have, by name. Each is a token of the theme, never a colour value.</summary>
    public static IReadOnlyList<string> Colours { get; } = ["default", "gray", "brown", "orange", "yellow", "green", "blue", "purple", "pink", "red"];

    /// <summary>The element types a view's settings are made of.</summary>
    public static IReadOnlyList<string> ViewSettings { get; } = ["Column", "Sort", "Condition", "FilterGroup", "GroupSetting"];

    /// <summary>The comparisons a filter has for each value type, besides <i>is empty</i> and <i>is not empty</i>, which every type has.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> Comparisons { get; } = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
    {
        ["text"] = ["is", "is-not", "contains", "does-not-contain", "starts-with", "ends-with"],
        ["number"] = ["equals", "does-not-equal", "greater-than", "less-than", "at-least", "at-most"],
        ["checkbox"] = ["is-checked", "is-not-checked"],
        ["date"] = ["is", "is-before", "is-after", "is-on-or-before", "is-on-or-after"],
        ["dateTime"] = ["is", "is-before", "is-after", "is-on-or-before", "is-on-or-after"],
        ["time"] = ["is", "is-before", "is-after", "is-on-or-before", "is-on-or-after"],
        ["selection"] = ["is", "is-not"],
        ["multipleSelection"] = ["contains", "does-not-contain"],
        ["relation"] = ["contains", "does-not-contain"],
    };

    /// <summary>
    /// The key a value of this type is stored under: an attribute of the cell for a type that holds
    /// one value, and of each of the cell's items for a type that holds several.
    /// </summary>
    public static string StoredKey(string valueType) => valueType switch
    {
        "checkbox" => "checked",
        "selection" or "multipleSelection" => "option",
        "relation" => "row",
        _ => valueType,
    };

    /// <summary>Whether a filter can compare a value of this type in this way.</summary>
    public static bool Compares(string valueType, string comparison) =>
        comparison is "is-empty" or "is-not-empty" || (Comparisons.TryGetValue(valueType, out var known) && known.Contains(comparison));
}
