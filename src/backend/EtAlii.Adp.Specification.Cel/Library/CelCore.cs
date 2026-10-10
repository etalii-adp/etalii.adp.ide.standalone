using System.Globalization;
using System.Text.RegularExpressions;

namespace EtAlii.Adp.Specification.Cel;

/// <summary>
/// Core CEL: the macros <c>all</c>, <c>exists</c>, <c>exists_one</c>, <c>filter</c> and <c>map</c>,
/// <c>size</c>, <c>matches</c> (over the regular expression subset of FBL §2.5) and the conversions
/// <c>int</c>, <c>double</c> and <c>string</c>, and <c>type</c> with the type names it is compared with.
/// </summary>
public static class CelCore
{
    public static void Register(CelEnvironment environment)
    {
        environment.AddMacro(new CelMacro("all", (items, body) => items.All(item => body(item) is not false)));
        environment.AddMacro(new CelMacro("exists", (items, body) => items.Any(item => body(item) is true)));
        environment.AddMacro(new CelMacro("exists_one", (items, body) => items.Count(item => body(item) is true) == 1));
        environment.AddMacro(new CelMacro("filter", (items, body) => items.Where(item => body(item) is true).ToList()));
        environment.AddMacro(new CelMacro("map", (items, body) => items.Select(body).ToList()));

        environment.AddFunction(CelFunction.Global("size", 1, a => Size(a[0])));
        environment.AddFunction(CelFunction.Receiver("size", 0, a => Size(a[0])));
        environment.AddFunction(CelFunction.Global("matches", 2, a => Matches(a[0], a[1]), Length));
        environment.AddFunction(CelFunction.Receiver("matches", 1, a => Matches(a[0], a[1]), Length));
        environment.AddFunction(CelFunction.Global("int", 1, a => Int(a[0])));
        environment.AddFunction(CelFunction.Global("double", 1, a => Double(a[0])));
        environment.AddFunction(CelFunction.Global("string", 1, a => CelValues.Format(a[0])));
        environment.AddFunction(CelFunction.Global("type", 1, a => CelType.Of(a[0])));
    }

    /// <summary>A cost proportional to the length of the first argument, for a function that walks it.</summary>
    internal static long Length(IReadOnlyList<object?> arguments) => arguments.Count == 0
        ? 1
        : arguments[0] switch
        {
            string s => 1 + (s.Length / 16),
            IReadOnlyList<object?> l => 1 + l.Count,
            IReadOnlyDictionary<string, object?> m => 1 + m.Count,
            _ => 1,
        };

    private static long Size(object? value) => value switch
    {
        string s => CodePoints.Count(s),
        IReadOnlyList<object?> l => l.Count,
        IReadOnlyDictionary<string, object?> m => m.Count,
        _ => throw new CelException("size() needs a string, a list or a map."),
    };

    // A pattern that does not parse, or a match past its time bound, is an error value like any failed call:
    // CelProgram.Evaluate turns only a CelException into one, so anything else would reach its caller.
    private static bool Matches(object? text, object? pattern)
    {
        try
        {
            return Regex.IsMatch(CelValues.AsString(text), RegexSubset.ToDotNet(CelValues.AsString(pattern), false), RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250));
        }
        catch (RegexMatchTimeoutException)
        {
            throw new CelException("matches() took longer than its time bound.");
        }
        catch (ArgumentException e)
        {
            throw new CelException($"matches() cannot use its pattern: {e.Message}");
        }
    }

    private static long Int(object? value) => value switch
    {
        long l => l,
        double d when double.IsFinite(d) && d is > long.MinValue and < long.MaxValue => (long)d,
        string s when long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) => v,
        _ => throw new CelException("int() cannot convert this value."),
    };

    private static double Double(object? value) => value is string s
        ? double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : throw new CelException($"double() cannot convert '{s}'.")
        : CelValues.AsDouble(value);
}
