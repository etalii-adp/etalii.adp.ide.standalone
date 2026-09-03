namespace EtAlii.Adp.Diagram.Sparql;

/// <summary>The expression-shaped constructs, which annotate rather than draw.</summary>
public enum SparqlConstraintKind
{
    /// <summary>A <c>FILTER</c>, attached to the group it constrains.</summary>
    Filter,

    /// <summary>A <c>BIND(... AS ?x)</c>, attached to the variable it defines.</summary>
    Bind,

    /// <summary>A <c>VALUES</c> block, attached to the variables it feeds.</summary>
    Values
}

/// <summary>
/// One expression-shaped constraint, kept exactly as written - the parser's second tier. The
/// text is sliced from the source, never rebuilt from a tree, because the requirements draw
/// expressions as annotations showing what the author wrote.
/// </summary>
/// <param name="Kind">Which construct this is.</param>
/// <param name="Text">The whole construct as written, keyword included.</param>
/// <param name="DefinedVariable">The variable a <c>BIND</c> defines, without its sigil; empty otherwise.</param>
/// <param name="ReferencedVariables">The variable names referenced inside, without sigils, in order of first mention - how the annotation finds its anchors.</param>
public sealed record SparqlConstraint(
    SparqlConstraintKind Kind,
    string Text,
    string DefinedVariable,
    IReadOnlyList<string> ReferencedVariables);
