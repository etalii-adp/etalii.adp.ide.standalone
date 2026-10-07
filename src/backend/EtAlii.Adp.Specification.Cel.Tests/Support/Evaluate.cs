namespace EtAlii.Adp.Specification.Cel.Tests;

/// <summary>Compiles and evaluates in one step, for tests that state an expression and its value.</summary>
internal static class Evaluate
{
    public static object? Expression(string expression, IReadOnlyDictionary<string, object?>? variables = null, CelEnvironment? environment = null)
    {
        environment ??= CelEnvironment.Standard();
        if (variables is not null) environment = environment.Clone().DeclareVariables(variables.Keys);
        return environment.Compile(expression).Evaluate(variables ?? new Dictionary<string, object?>());
    }

    /// <summary>A value as text a test can compare: lists joined with commas, optionals spelled out.</summary>
    public static string Text(object? value) => value switch
    {
        null => "null",
        string s => s,
        bool b => b ? "true" : "false",
        long l => l.ToString(System.Globalization.CultureInfo.InvariantCulture),
        double d => d.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
        IReadOnlyList<object?> list => "[" + string.Join(",", list.Select(Text)) + "]",
        CelOptional { HasValue: true } o => $"optional.of({Text(o.Value)})",
        CelOptional => "optional.none()",
        CelError e => $"error: {e.Message}",
        _ => value.ToString() ?? "",
    };
}
