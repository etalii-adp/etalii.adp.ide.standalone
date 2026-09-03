namespace EtAlii.Adp.Diagram.Sparql;

/// <summary>The kinds of group a where clause is built of - the scope tree's node vocabulary.</summary>
public enum GroupScopeKind
{
    /// <summary>A plain group: the root where clause, or a braced group inside it.</summary>
    Group,

    /// <summary>An <c>OPTIONAL</c> group - drawn as a dashed containment region.</summary>
    Optional,

    /// <summary>A <c>UNION</c>: the alternatives live in its <see cref="GroupScope.Children"/> as branches.</summary>
    Union,

    /// <summary>One branch of a <c>UNION</c>.</summary>
    Branch,

    /// <summary>A <c>MINUS</c> group - a region labeled as exclusion.</summary>
    Minus,

    /// <summary>A <c>GRAPH ?g</c>/<c>GRAPH &lt;iri&gt;</c> group, labeled with its graph term.</summary>
    Graph,

    /// <summary>A <c>SERVICE</c> group, labeled with its endpoint - drawn, never contacted.</summary>
    Service,

    /// <summary>A <c>CONSTRUCT</c> template: the shape being built, beside the where clause being matched.</summary>
    Template
}
