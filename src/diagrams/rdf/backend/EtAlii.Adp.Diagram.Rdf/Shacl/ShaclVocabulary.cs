namespace EtAlii.Adp.Diagram.Rdf.Shacl;

/// <summary>
/// The closed SHACL Core table, sourced from the recommendation (w3.org/TR/shacl): the terms the
/// discovery, projection, writers and validator speak of by name, and the full known-term set the
/// unknown-term finding checks against. A data table, not logic (shacl-diagram Requirement 1.1).
/// </summary>
public static class ShaclVocabulary
{
    /// <summary>The SHACL namespace every table entry lives in.</summary>
    public const string Namespace = "http://www.w3.org/ns/shacl#";

    public const string NodeShape = Namespace + "NodeShape";
    public const string PropertyShape = Namespace + "PropertyShape";

    public const string TargetClass = Namespace + "targetClass";
    public const string TargetNode = Namespace + "targetNode";
    public const string TargetSubjectsOf = Namespace + "targetSubjectsOf";
    public const string TargetObjectsOf = Namespace + "targetObjectsOf";

    public const string Path = Namespace + "path";
    public const string Node = Namespace + "node";
    public const string Property = Namespace + "property";
    public const string QualifiedValueShape = Namespace + "qualifiedValueShape";
    public const string And = Namespace + "and";
    public const string Or = Namespace + "or";
    public const string Xone = Namespace + "xone";
    public const string Not = Namespace + "not";

    public const string Class = Namespace + "class";
    public const string Datatype = Namespace + "datatype";
    public const string NodeKind = Namespace + "nodeKind";
    public const string MinCount = Namespace + "minCount";
    public const string MaxCount = Namespace + "maxCount";
    public const string HasValue = Namespace + "hasValue";
    public const string In = Namespace + "in";
    public const string Closed = Namespace + "closed";
    private const string IgnoredProperties = Namespace + "ignoredProperties";
    public const string Sparql = Namespace + "sparql";
    public const string Select = Namespace + "select";

    public const string Deactivated = Namespace + "deactivated";
    public const string Severity = Namespace + "severity";
    public const string Message = Namespace + "message";
    public const string Name = Namespace + "name";
    public const string Description = Namespace + "description";

    public const string Violation = Namespace + "Violation";
    private const string Warning = Namespace + "Warning";
    private const string Info = Namespace + "Info";

    public const string InversePath = Namespace + "inversePath";
    public const string AlternativePath = Namespace + "alternativePath";
    public const string ZeroOrMorePath = Namespace + "zeroOrMorePath";
    public const string OneOrMorePath = Namespace + "oneOrMorePath";
    public const string ZeroOrOnePath = Namespace + "zeroOrOnePath";

    /// <summary>What the implicit class target is defined against (RDFS, not SHACL, but spoken of here).</summary>
    public const string RdfsClass = "http://www.w3.org/2000/01/rdf-schema#Class";

    /// <summary>The subclass relation the implicit-class walk follows, as stated in the file only.</summary>
    public const string RdfsSubClassOf = "http://www.w3.org/2000/01/rdf-schema#subClassOf";

    /// <summary>The predicates whose subject is a target-carrying shape.</summary>
    public static readonly IReadOnlySet<string> TargetPredicates = new HashSet<string>(StringComparer.Ordinal)
    {
        TargetClass, TargetNode, TargetSubjectsOf, TargetObjectsOf,
    };

    /// <summary>
    /// The constraint component parameters of SHACL Core (plus <c>sh:sparql</c> from SHACL-SPARQL):
    /// a subject of any of these is a shape by use, per the recommendation's definition.
    /// </summary>
    public static readonly IReadOnlySet<string> ConstraintParameters = new HashSet<string>(StringComparer.Ordinal)
    {
        Class, Datatype, NodeKind, MinCount, MaxCount,
        Namespace + "minExclusive", Namespace + "minInclusive", Namespace + "maxExclusive", Namespace + "maxInclusive",
        Namespace + "minLength", Namespace + "maxLength", Namespace + "pattern", Namespace + "flags",
        Namespace + "languageIn", Namespace + "uniqueLang",
        Namespace + "equals", Namespace + "disjoint", Namespace + "lessThan", Namespace + "lessThanOrEquals",
        Not, And, Or, Xone,
        Node, Property, QualifiedValueShape, Namespace + "qualifiedMinCount", Namespace + "qualifiedMaxCount",
        Namespace + "qualifiedValueShapesDisjoint",
        Closed, IgnoredProperties, HasValue, In,
        Sparql,
    };

    /// <summary>The predicates whose single object is itself a shape.</summary>
    public static readonly IReadOnlySet<string> ShapeExpectingPredicates = new HashSet<string>(StringComparer.Ordinal)
    {
        Node, Property, QualifiedValueShape, Not,
    };

    /// <summary>The predicates whose object is a list of shapes.</summary>
    public static readonly IReadOnlySet<string> ShapeListPredicates = new HashSet<string>(StringComparer.Ordinal)
    {
        And, Or, Xone,
    };

    /// <summary>
    /// Every term the recommendation defines in the SHACL namespace, for the unknown-term finding:
    /// a <c>sh:</c> IRI outside this set is the typo that silently disables a constraint
    /// (Requirement 7.4). The union of the tables above plus the terms code never names directly.
    /// </summary>
    public static readonly IReadOnlySet<string> AllTerms = BuildAllTerms();

    private static HashSet<string> BuildAllTerms()
    {
        var terms = new HashSet<string>(StringComparer.Ordinal)
        {
            NodeShape, PropertyShape, Namespace + "Shape",
            Path, Deactivated, Severity, Message, Name, Description,
            Violation, Warning, Info, Namespace + "Severity",
            InversePath, AlternativePath, ZeroOrMorePath, OneOrMorePath, ZeroOrOnePath,
            Namespace + "IRI", Namespace + "BlankNode", Namespace + "Literal",
            Namespace + "BlankNodeOrIRI", Namespace + "BlankNodeOrLiteral", Namespace + "IRIOrLiteral",
            Namespace + "order", Namespace + "group", Namespace + "defaultValue", Namespace + "PropertyGroup",
            Select, Namespace + "ask", Namespace + "construct", Namespace + "update",
            Namespace + "prefixes", Namespace + "declare", Namespace + "prefix", Namespace + "namespace",
            Namespace + "SPARQLConstraint", Namespace + "shapesGraph", Namespace + "entailment",
            Namespace + "ValidationReport", Namespace + "conforms", Namespace + "result",
            Namespace + "ValidationResult", Namespace + "focusNode", Namespace + "resultPath",
            Namespace + "resultMessage", Namespace + "resultSeverity", Namespace + "sourceConstraint",
            Namespace + "sourceConstraintComponent", Namespace + "sourceShape", Namespace + "value",
            Namespace + "detail",
        };
        terms.UnionWith(TargetPredicates);
        terms.UnionWith(ConstraintParameters);
        return terms;
    }
}
