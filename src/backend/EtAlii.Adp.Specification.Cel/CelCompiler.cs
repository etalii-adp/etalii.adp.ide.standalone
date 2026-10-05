namespace EtAlii.Adp.Specification.Cel;

/// <summary>
/// The subset of CEL this library evaluates: literals, member access and indexing, the logical,
/// relational and arithmetic operators, <c>in</c>, the conditional, <c>has()</c>, the macros
/// <c>all</c>, <c>exists</c>, <c>exists_one</c>, <c>filter</c> and <c>map</c>, and the functions
/// <c>size</c>, <c>matches</c>, <c>startsWith</c>, <c>endsWith</c>, <c>contains</c>, <c>replace</c>,
/// <c>lowerAscii</c>, <c>upperAscii</c>, <c>int</c>, <c>double</c> and <c>string</c>. Any other
/// construct is refused when the expression is compiled, naming the construct, so a binding that
/// needs more fails at load rather than at read.
/// </summary>
public static class CelCompiler
{
    /// <summary>Compiles <paramref name="expression"/>, which may use only <paramref name="variables"/> and the names its macros bind.</summary>
    public static CelProgram Compile(string expression, IReadOnlyList<string> variables)
    {
        var parser = new CelParser(expression);
        var node = parser.ParseExpression();
        parser.ExpectEnd();
        Check(node, variables, []);
        return new CelProgram(expression, node);
    }

    private static void Check(CelNode node, IReadOnlyList<string> variables, HashSet<string> bound)
    {
        switch (node)
        {
            case CelNode.Ident ident:
                if (!variables.Contains(ident.Name) && !bound.Contains(ident.Name))
                {
                    throw new CelException($"'{ident.Name}' is not a variable here; available: {string.Join(", ", variables)}.");
                }
                break;
            case CelNode.Macro macro:
                Check(macro.Target, variables, bound);
                var inner = new HashSet<string>(bound) { macro.Variable };
                Check(macro.Body, variables, inner);
                break;
            default:
                foreach (var child in node.Children) Check(child, variables, bound);
                break;
        }
    }
}
