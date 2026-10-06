using EtAlii.Adp.Specification.Cel;

namespace EtAlii.Adp.Specification.Disl;

/// <summary>
/// The DISL function library (§12.4) and the element methods of §12.2 that this runtime offers, on
/// top of CEL's standard environment. A specification that calls anything else is refused at load,
/// naming it.
/// </summary>
/// <remarks>
/// The element methods are declared only: <c>isA</c>, <c>descendants</c> and the rest are
/// implemented by the model's elements (<see cref="ICelObject"/>), and calling one on any other value
/// is an evaluation error.
/// </remarks>
public static class DislCelLibrary
{
    /// <summary>The methods of DISL's Element and Diagram types (§12.2) this runtime implements, with their arities.</summary>
    private static readonly (string Name, int Min, int Max)[] Methods =
    [
        ("isA", 1, 1),
        ("descendants", 0, 0),
        ("ancestors", 0, 0),
        ("childrenOfType", 1, 1),
        ("incomingOf", 1, 1),
        ("outgoingOf", 1, 1),
        ("positionIn", 1, 1),
        ("nodesOfType", 1, 2),
        ("relationsOfType", 1, 1),
        ("elementById", 1, 1),
        ("label", 0, 0),
        ("other", 1, 1),
    ];

    /// <summary>Adds the library to <paramref name="environment"/>; <paramref name="enums"/> finds the enumerations <c>enumLabel</c> reads.</summary>
    public static CelEnvironment Register(CelEnvironment environment, Func<string, DislEnum?> enums)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(enums);

        foreach ((string name, int min, int max) in Methods)
        {
            environment.AddFunction(CelFunction.Method(name, min, max));
        }

        environment.AddFunction(CelFunction.Global("enumLabel", 2, arguments => EnumLabel(enums, arguments[0], arguments[1])));
        environment.AddFunction(CelFunction.Global("clamp", 3, arguments => Clamp(arguments[0], arguments[1], arguments[2])));
        environment.AddFunction(new CelFunction("min", CelCallStyle.Global, 2, 4, call => Extreme(call.Arguments, -1)));
        environment.AddFunction(new CelFunction("max", CelCallStyle.Global, 2, 4, call => Extreme(call.Arguments, 1)));
        environment.AddFunction(CelFunction.Global("yearMonth", 2, arguments => YearMonth.Of(Int(arguments[0]), Int(arguments[1]))));
        environment.AddFunction(CelFunction.Global("formatYearMonth", 2, arguments => YearMonth.Format(Int(arguments[0]), Text(arguments[1]))));
        environment.AddFunction(CelFunction.Global("parseYearMonth", 1, arguments =>
            YearMonth.Parse(Text(arguments[0])) is { } index ? CelOptional.Of(index) : CelOptional.None));
        environment.AddFunction(CelFunction.Receiver("year", 0, arguments => YearMonth.YearOf(Int(arguments[0]))));
        environment.AddFunction(CelFunction.Receiver("month", 0, arguments => YearMonth.MonthOf(Int(arguments[0]))));
        return environment;
    }

    /// <summary>The label of an enumeration's value (§4.5): its declared label, else its key; a value the enumeration lacks, as an extensible one may hold, is its own label.</summary>
    private static string EnumLabel(Func<string, DislEnum?> enums, object? name, object? value)
    {
        var enumeration = enums(Text(name)) ?? throw new CelException($"'{name}' is not an enumeration of this specification.");
        var key = Text(value);
        return enumeration.ValueOf(key) is { } known ? known.Label ?? known.Key : key;
    }

    /// <summary><c>clamp(v, lo, hi)</c>: <c>lo</c> below it, <c>hi</c> above it, otherwise <c>v</c> itself, of whichever kind each is.</summary>
    private static object? Clamp(object? value, object? low, object? high) =>
        Number(value) < Number(low) ? low : Number(value) > Number(high) ? high : value;

    /// <summary>The smallest (<paramref name="sign"/> -1) or largest (1) argument, itself rather than converted, the first of equals; as <c>math.least</c> and <c>math.greatest</c>.</summary>
    private static object? Extreme(IReadOnlyList<object?> arguments, int sign)
    {
        var best = arguments[0];
        foreach (var candidate in arguments.Skip(1))
        {
            if (Number(candidate).CompareTo(Number(best)) * sign > 0) best = candidate;
        }
        return best;
    }

    private static double Number(object? value) => value switch
    {
        long integer => integer,
        double real => real,
        _ => throw new CelException("A number was expected."),
    };

    private static long Int(object? value) => value as long? ?? throw new CelException("An int was expected.");

    private static string Text(object? value) => value as string ?? throw new CelException("A string was expected.");
}
