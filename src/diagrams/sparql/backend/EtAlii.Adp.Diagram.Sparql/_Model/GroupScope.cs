namespace EtAlii.Adp.Diagram.Sparql;

/// <summary>
/// One node of the scope tree: a group of triple patterns, the groups nested inside it, and the
/// constraints attached to it. The tree is the query's structure; everything expression-shaped
/// hangs off it as text kept as written.
/// </summary>
public sealed class GroupScope
{
    /// <summary>What kind of group this is.</summary>
    public required GroupScopeKind Kind { get; init; }

    /// <summary>
    /// This scope's position among same-kind siblings, in source order - one half of the stable
    /// element id (<c>optional.1</c> is a parent's second optional child), so ids survive a
    /// reparse of an unchanged file.
    /// </summary>
    public required int Ordinal { get; init; }

    /// <summary>
    /// The label a labeled group carries: the graph term for <see cref="GroupScopeKind.Graph"/>,
    /// the endpoint for <see cref="GroupScopeKind.Service"/> (with <c>SILENT</c> when written),
    /// empty otherwise. As written, always.
    /// </summary>
    public string Label { get; init; } = "";

    /// <summary>This scope's own triple patterns, in source order.</summary>
    public List<TriplePattern> Patterns { get; } = [];

    /// <summary>The groups nested inside this one, in source order.</summary>
    public List<GroupScope> Children { get; } = [];

    /// <summary>The <c>FILTER</c>/<c>BIND</c>/<c>VALUES</c> constraints attached to this scope, as written.</summary>
    public List<SparqlConstraint> Constraints { get; } = [];

    /// <summary>The subqueries nested in this scope, each collapsed to its projection.</summary>
    public List<SparqlSubSelect> SubSelects { get; } = [];

    /// <summary>
    /// The dotted path naming this scope from the root - <c>where</c>, or
    /// <c>where/optional.0</c> and deeper - the stable half of every region-derived id.
    /// </summary>
    public string Path { get; internal set; } = "";
}
