namespace EtAlii.Adp.Specification.Cel;

/// <summary>A compiled CEL expression.</summary>
public sealed class CelProgram
{
    private readonly long _budget;

    internal CelProgram(string source, CelNode root, long budget)
    {
        Source = source;
        Root = root;
        _budget = budget;
    }

    public string Source { get; }

    private CelNode Root { get; }

    /// <summary>Evaluates with <paramref name="variables"/>; a value of <see cref="CelError"/> when evaluation fails.</summary>
    public object? Evaluate(IReadOnlyDictionary<string, object?> variables) => Evaluate(variables, new CelBudget(_budget));

    /// <summary>
    /// Evaluates with <paramref name="variables"/>, charging <paramref name="budget"/>: how a function
    /// that evaluates another expression keeps the whole evaluation within one limit.
    /// </summary>
    public object? Evaluate(IReadOnlyDictionary<string, object?> variables, CelBudget budget)
    {
        try
        {
            return Root.Evaluate(new CelScope(variables, budget));
        }
        catch (CelException e)
        {
            return new CelError(e.Message);
        }
    }

    /// <summary>True only when the expression evaluates to true.</summary>
    public bool IsTrue(IReadOnlyDictionary<string, object?> variables) => Evaluate(variables) is true;
}
