namespace EtAlii.Adp.Specification.Cel;

/// <summary>
/// CEL's math extension: <c>math.round</c> (halves away from zero), <c>math.floor</c>,
/// <c>math.ceil</c> and <c>math.trunc</c>, which give doubles; <c>math.abs</c>, which keeps an int an
/// int; and <c>math.greatest</c> and <c>math.least</c> over arguments or one list, comparing ints and
/// doubles by value and giving the winner unchanged.
/// </summary>
public static class CelMath
{
    public static void Register(CelEnvironment environment)
    {
        environment.AddFunction(CelFunction.Global("math.round", 1, a => Math.Round(CelValues.AsDouble(a[0]), MidpointRounding.AwayFromZero)));
        environment.AddFunction(CelFunction.Global("math.floor", 1, a => Math.Floor(CelValues.AsDouble(a[0]))));
        environment.AddFunction(CelFunction.Global("math.ceil", 1, a => Math.Ceiling(CelValues.AsDouble(a[0]))));
        environment.AddFunction(CelFunction.Global("math.trunc", 1, a => Math.Truncate(CelValues.AsDouble(a[0]))));
        environment.AddFunction(CelFunction.Global("math.abs", 1, a => Abs(a[0])));
        environment.AddFunction(new CelFunction("math.greatest", CelCallStyle.Global, 1, CelFunction.Variadic, call => Extreme("math.greatest", call.Arguments, 1), Count));
        environment.AddFunction(new CelFunction("math.least", CelCallStyle.Global, 1, CelFunction.Variadic, call => Extreme("math.least", call.Arguments, -1), Count));
    }

    private static object Abs(object? value) => value switch
    {
        long.MinValue => throw new CelException("math.abs() overflows an int."),
        long l => (object)Math.Abs(l),
        double d => Math.Abs(d),
        _ => throw new CelException("math.abs() needs a number."),
    };

    private static object? Extreme(string name, IReadOnlyList<object?> arguments, int sign)
    {
        var values = arguments is [IReadOnlyList<object?> list] ? list : arguments;
        if (values.Count == 0) throw new CelException($"{name}() needs at least one number.");
        var best = values[0];
        CelValues.AsDouble(best);
        foreach (var value in values.Skip(1))
        {
            if (CelValues.Compare(value, best) * sign > 0) best = value;
        }
        return best;
    }

    private static long Count(IReadOnlyList<object?> arguments) => arguments is [IReadOnlyList<object?> list] ? 1 + list.Count : arguments.Count;
}
