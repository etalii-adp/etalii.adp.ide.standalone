namespace EtAlii.Adp.Specification.Cel;

/// <summary>A compiled CEL expression.</summary>
public sealed class CelProgram
{
    private const int StepBudget = 100_000;

    internal CelProgram(string source, CelNode root)
    {
        Source = source;
        Root = root;
    }

    public string Source { get; }

    internal CelNode Root { get; }

    /// <summary>Evaluates with <paramref name="variables"/>; a value of <see cref="CelError"/> when evaluation fails.</summary>
    public object? Evaluate(IReadOnlyDictionary<string, object?> variables)
    {
        var steps = 0;
        try
        {
            return Root.Evaluate(new CelScope(variables, null), ref steps);
        }
        catch (CelException e)
        {
            return new CelError(e.Message);
        }
    }

    /// <summary>True only when the expression evaluates to true.</summary>
    public bool IsTrue(IReadOnlyDictionary<string, object?> variables) => Evaluate(variables) is true;

    internal static void Step(ref int steps)
    {
        if (++steps > StepBudget) throw new CelException("The expression exceeded its evaluation budget.");
    }
}
