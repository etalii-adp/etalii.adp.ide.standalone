namespace EtAlii.Adp.Designer.Knowledge;

/// <summary>
/// The value types a property can have, each with its strategy (<see cref="KnowledgeValueType"/>):
/// the one place their names are written, and where a property's type is looked up by its name.
/// </summary>
/// <remarks>
/// <b>Written out rather than read from <c>knowledge.des</c>, on purpose</b>, as the rest of
/// <see cref="KnowledgeVocabulary"/> is: a test fails when the two differ, in either direction.
/// </remarks>
internal static class KnowledgeValueTypes
{
    /// <summary>Text: what a title is, and what a property without a type given is made as.</summary>
    public static KnowledgeValueType Text { get; } = new KnowledgeTextType();

    /// <summary>A relation: what a property is made as by choosing a table to relate to, and never by a change of type.</summary>
    public static KnowledgeValueType Relation { get; } = new KnowledgeRelationType();

    /// <summary>Every value type, in the specification's order.</summary>
    public static IReadOnlyList<KnowledgeValueType> All { get; } =
    [
        Text,
        new KnowledgeNumberType(),
        new KnowledgeCheckboxType(),
        new KnowledgeDateType(),
        new KnowledgeDateTimeType(),
        new KnowledgeTimeType(),
        new KnowledgeSelectionType(),
        new KnowledgeMultipleSelectionType(),
        Relation,
    ];

    private static readonly Dictionary<string, KnowledgeValueType> _byName = All.ToDictionary(type => type.Name, StringComparer.Ordinal);

    /// <summary>
    /// The type of this name. A name the specification does not have - written by hand, or by a
    /// later version, or empty - is a type of its own (<see cref="KnowledgeUnknownType"/>): its
    /// values are kept as they are, shown as text, and cannot be changed here.
    /// </summary>
    public static KnowledgeValueType Of(string name) => _byName.GetValueOrDefault(name) ?? new KnowledgeUnknownType(name);
}
