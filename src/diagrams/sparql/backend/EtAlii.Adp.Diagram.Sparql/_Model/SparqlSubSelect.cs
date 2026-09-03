namespace EtAlii.Adp.Diagram.Sparql;

/// <summary>
/// A subquery, collapsed: one node stating its projection, its internals deliberately not
/// modeled beyond the text a panel can show - one level of honesty beats nested unreadability
/// (Requirement 3.6). Its projected names are the join surface it offers the outer query.
/// </summary>
/// <param name="ProjectionText">The projection as written, e.g. <c>SELECT ?x (COUNT(?y) AS ?n)</c>.</param>
/// <param name="Text">The whole subquery as written, braces included, for the property grid.</param>
/// <param name="ProjectedNames">The names the subquery projects into the outer scope, without sigils.</param>
/// <param name="ProjectsAll">Whether the subquery wrote <c>SELECT *</c>; then <paramref name="ProjectedNames"/> holds what could be read from its own where clause.</param>
/// <param name="Ordinal">Position among the parent scope's subqueries, in source order.</param>
public sealed record SparqlSubSelect(
    string ProjectionText,
    string Text,
    IReadOnlyList<string> ProjectedNames,
    bool ProjectsAll,
    int Ordinal);
