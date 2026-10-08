namespace EtAlii.Adp.Diagram.Rdf;

/// <summary>The SKOS terms the scheme reading speaks of by name (skos-diagram Requirement 1).</summary>
public static class SkosVocabulary
{
    private const string Skos = "http://www.w3.org/2004/02/skos/core#";
    private const string SkosXl = "http://www.w3.org/2008/05/skos-xl#";

    public const string Concept = Skos + "Concept";
    public const string ConceptScheme = Skos + "ConceptScheme";
    public const string Collection = Skos + "Collection";
    public const string OrderedCollection = Skos + "OrderedCollection";

    public const string InScheme = Skos + "inScheme";
    public const string TopConceptOf = Skos + "topConceptOf";
    public const string HasTopConcept = Skos + "hasTopConcept";

    public const string Broader = Skos + "broader";
    public const string Narrower = Skos + "narrower";
    public const string Related = Skos + "related";

    public const string PrefLabel = Skos + "prefLabel";
    public const string AltLabel = Skos + "altLabel";
    public const string HiddenLabel = Skos + "hiddenLabel";
    public const string Notation = Skos + "notation";

    public const string Member = Skos + "member";
    public const string MemberList = Skos + "memberList";

    private const string ExactMatch = Skos + "exactMatch";
    private const string CloseMatch = Skos + "closeMatch";
    private const string BroadMatch = Skos + "broadMatch";
    private const string NarrowMatch = Skos + "narrowMatch";
    private const string RelatedMatch = Skos + "relatedMatch";

    /// <summary>The documentation properties the property grid edits, in the grid's own order.</summary>
    public static readonly IReadOnlyList<string> Documentation =
    [
        Skos + "definition",
        Skos + "scopeNote",
        Skos + "example",
        Skos + "note",
        Skos + "historyNote",
        Skos + "editorialNote",
        Skos + "changeNote",
    ];

    /// <summary>The mapping properties, drawn only when both ends are in-file concepts (Requirement 1.3).</summary>
    public static readonly IReadOnlyList<string> Mappings =
        [ExactMatch, CloseMatch, BroadMatch, NarrowMatch, RelatedMatch];

    /// <summary>
    /// The SKOS-XL label properties. Their indirection is deliberately unread (Requirement 3.6);
    /// their presence is what the validator reports once per file.
    /// </summary>
    public static readonly IReadOnlyList<string> XlLabelProperties =
        [SkosXl + "prefLabel", SkosXl + "altLabel", SkosXl + "hiddenLabel"];
}
