namespace EtAlii.Adp.Designer.Knowledge;

/// <summary>
/// The words of the designer's specification that this module implements, written out: the value
/// types, the key each is stored under, the comparisons each can be filtered by, and what a view
/// stores. The code that reads, edits and arranges a table goes by these. What a value type means
/// is its strategy's to say (<see cref="KnowledgeValueTypes"/>); its words here are read from there,
/// so there is one list.
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
    /// <summary>The value types a property can have, as <see cref="KnowledgeValueTypes"/> has them.</summary>
    public static IReadOnlyList<string> ValueTypes { get; } = [.. KnowledgeValueTypes.All.Select(type => type.Name)];

    /// <summary>The keys a cell or a condition holds its one value under: those of the types that hold one, in their order.</summary>
    public static IReadOnlyList<string> ValueKeys { get; } = [.. KnowledgeValueTypes.All.Where(type => !type.HoldsSeveral).Select(type => type.StoredKey).Distinct()];

    /// <summary>The colours an option can have, by name. Each is a token of the theme, never a colour value.</summary>
    public static IReadOnlyList<string> Colours { get; } = ["default", "gray", "brown", "orange", "yellow", "green", "blue", "purple", "pink", "red"];

    /// <summary>The element types a view's settings are made of.</summary>
    public static IReadOnlyList<string> ViewSettings { get; } = ["Column", "Sort", "Condition", "FilterGroup", "GroupSetting"];

    /// <summary>The comparisons a filter has for each value type, besides <i>is empty</i> and <i>is not empty</i>, which every type has.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> Comparisons { get; } = KnowledgeValueTypes.All.ToDictionary(type => type.Name, type => type.Comparisons, StringComparer.Ordinal);
}
