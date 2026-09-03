namespace EtAlii.Adp.Diagram.Sparql;

/// <summary>The four query forms of SPARQL 1.1.</summary>
public enum SparqlQueryForm
{
    /// <summary>A <c>SELECT</c> query: a table of bindings.</summary>
    Select,

    /// <summary>A <c>CONSTRUCT</c> query: a graph built from a template.</summary>
    Construct,

    /// <summary>An <c>ASK</c> query: a yes or no.</summary>
    Ask,

    /// <summary>A <c>DESCRIBE</c> query: whatever the endpoint says describes the targets.</summary>
    Describe
}

/// <summary>One projected item of a <c>SELECT</c> clause.</summary>
/// <param name="Name">The projected variable's name, without its sigil.</param>
/// <param name="ExpressionText">The defining expression for <c>(expr AS ?name)</c> items, as written; empty for a plain variable.</param>
public sealed record SparqlProjectionItem(string Name, string ExpressionText);

/// <summary>One <c>PREFIX</c> declaration of the prologue.</summary>
/// <param name="Prefix">The prefix without its colon; empty for the default prefix.</param>
/// <param name="Iri">The namespace IRI it expands to.</param>
/// <param name="Line">The 1-based line the declaration sits on - what an unused-prefix finding points at.</param>
public sealed record SparqlPrefixDeclaration(string Prefix, string Iri, int Line);

/// <summary>
/// How one variable name is used across the whole query - the query-wide occurrence index the
/// requirements name. A variable's <see cref="PatternOccurrences"/> is its join count: the
/// number of triple-pattern positions that mention it, which is the degree its one node will
/// have on the canvas.
/// </summary>
/// <param name="Name">The variable's name, without its sigil.</param>
/// <param name="PatternOccurrences">How many triple-pattern positions mention it.</param>
/// <param name="Projected">Whether the query's own projection carries it outward.</param>
/// <param name="DefiningExpression">The expression that defines it (<c>BIND</c> or a projection alias), as written; empty for an ordinary variable.</param>
/// <param name="ScopePaths">The paths of every scope that mentions it, in first-mention order - the shallowest is where its node will live.</param>
public sealed record SparqlVariableUsage(
    string Name,
    int PatternOccurrences,
    bool Projected,
    string DefiningExpression,
    IReadOnlyList<string> ScopePaths);

/// <summary>
/// What a query file says: the prologue, the form and its frame, the scope tree of graph
/// patterns, and the query-wide variable index. This is this module's own model - deliberately
/// not the RDF family's <c>RdfModel</c>, because a query is not a serialization of a graph.
/// </summary>
public sealed record SparqlQueryModel
{
    /// <summary>The <c>BASE</c> IRI, empty when none is declared.</summary>
    public string BaseIri { get; init; } = "";

    /// <summary>The prologue's prefix declarations, in source order.</summary>
    public IReadOnlyList<SparqlPrefixDeclaration> Prefixes { get; init; } = [];

    /// <summary>Which of the four forms the query is.</summary>
    public SparqlQueryForm Form { get; init; } = SparqlQueryForm.Select;

    /// <summary>Whether the form wrote <c>DISTINCT</c>.</summary>
    public bool Distinct { get; init; }

    /// <summary>Whether the form wrote <c>REDUCED</c>.</summary>
    public bool Reduced { get; init; }

    /// <summary>Whether the projection is <c>SELECT *</c>.</summary>
    public bool ProjectsAll { get; init; }

    /// <summary>The projected items; for <c>SELECT *</c>, the in-scope names that resolves to.</summary>
    public IReadOnlyList<SparqlProjectionItem> Projection { get; init; } = [];

    /// <summary>A <c>DESCRIBE</c> query's targets, as written; empty for the other forms.</summary>
    public IReadOnlyList<string> DescribeTargets { get; init; } = [];

    /// <summary>The <c>FROM</c>/<c>FROM NAMED</c> clauses, as written - frame, not structure.</summary>
    public IReadOnlyList<string> DatasetClauses { get; init; } = [];

    /// <summary>A <c>CONSTRUCT</c> query's template scope; null for the other forms.</summary>
    public GroupScope? Template { get; init; }

    /// <summary>The root of the where clause's scope tree; empty patterns for a <c>DESCRIBE</c> with no <c>WHERE</c>.</summary>
    public GroupScope Where { get; init; } = new() { Kind = GroupScopeKind.Group, Ordinal = 0, Path = "where" };

    /// <summary>The solution modifiers as written rows - <c>GROUP BY ?x</c>, <c>ORDER BY DESC(?d)</c>, <c>LIMIT 100</c> - header content, never canvas structure.</summary>
    public IReadOnlyList<string> ModifierRows { get; init; } = [];

    /// <summary>The query-wide variable index, keyed by name.</summary>
    public IReadOnlyDictionary<string, SparqlVariableUsage> Variables { get; init; } =
        new Dictionary<string, SparqlVariableUsage>();

    /// <summary>The model a file that could not be parsed carries: nothing.</summary>
    public static SparqlQueryModel Empty { get; } = new();
}
