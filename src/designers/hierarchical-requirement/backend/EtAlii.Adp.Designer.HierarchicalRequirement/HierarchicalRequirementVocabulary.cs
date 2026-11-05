namespace EtAlii.Adp.Designer.HierarchicalRequirement;

/// <summary>
/// The words of the designer's specification that this module implements, written out: its name, the element types and
/// their attributes, the document's attributes, the enumerations and their values, and the operations.
/// </summary>
/// <remarks>
/// <b>Written out rather than read from <c>hierarchical-requirement.des</c>, on purpose</b>, as the Knowledge module
/// writes its own: a module that read its vocabulary from the specification would agree with it by construction and
/// implement none of what was added to it. Written here, the two can differ, and a test fails when they do, in either
/// direction.
/// </remarks>
internal static class HierarchicalRequirementVocabulary
{
    /// <summary>The designer type's name, as its specification labels it.</summary>
    public const string Name = "Hierarchical Requirement Designer";

    /// <summary>The element types and the attributes of each, in the specification's order.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> Types { get; } = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
    {
        ["Item"] = ["title", "description", "tags", "collapsed"],
        ["Requirement"] = ["title", "description", "tags", "category", "state", "collapsed"],
        ["Assessment"] = ["kind", "about", "text", "detail", "resolved"],
        ["Tag"] = ["name", "colour"],
        ["Category"] = ["name", "colour"],
        ["State"] = ["name", "colour"],
        ["Filter"] = ["name", "query"],
        ["Column"] = ["level", "width"],
    };

    /// <summary>The document's own attributes: the marks of the two counters, on line 2 of the file.</summary>
    public static IReadOnlyList<string> DocumentAttributes { get; } = ["lastItem", "lastRequirement"];

    /// <summary>The enumerations and their values, by key.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> Enumerations { get; } = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
    {
        ["Colour"] = ["default", "gray", "brown", "orange", "yellow", "green", "blue", "purple", "pink", "red"],
        ["AssessmentKind"] = [.. Enum.GetValues<AssessmentKind>().Select(kind => kind.ToString().ToLowerInvariant())],
    };

    /// <summary>The operations the designer offers, by name.</summary>
    public static IReadOnlyList<string> Operations { get; } =
    [
        "addTopItem", "addItem", "addItemBelow", "addRequirement", "addRequirementBelow", "setTitle", "setDescription", "moveItem", "moveRequirement", "deleteEntry", "toggleCollapsed",
        "addTag", "removeTag", "renameTag", "recolourTag", "moveTag", "deleteTag",
        "setCategory", "clearCategory", "addCategory", "renameCategory", "recolourCategory", "moveCategory", "deleteCategory",
        "setState", "clearState", "addState", "renameState", "recolourState", "moveState", "deleteState",
        "addAssessment", "changeAssessment", "resolveAssessment", "removeAssessment",
        "storeFilter", "renameFilter", "removeFilter",
        "addColumnWidth", "setColumnWidth", "resetColumnWidth",
    ];
}
