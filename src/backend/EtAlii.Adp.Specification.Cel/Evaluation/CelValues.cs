using System.Globalization;

namespace EtAlii.Adp.Specification.Cel;

internal static class CelValues
{
    // The tolerance EtAlii.Adp shares as double.Tolerance, kept here so this project references none of the solution.
    private const double Tolerance = 0.000001f;

    public static string AsString(object? value) => value as string ?? throw new CelException("A string was expected.");

    public static long AsInt(object? value) => value is long l ? l : throw new CelException("An int was expected.");

    public static bool AsBool(object? value) => value is bool b ? b : throw new CelException("A bool was expected.");

    public static double AsDouble(object? value) => value switch
    {
        long l => l,
        double d => d,
        _ => throw new CelException("A number was expected."),
    };

    public static IReadOnlyList<object?> AsList(object? value) => value as IReadOnlyList<object?> ?? throw new CelException("A list was expected.");

    public static bool Equal(object? a, object? b) => (a, b) switch
    {
        (null, null) => true,
        (long x, double y) => Math.Abs(x - y) < Tolerance,
        (double x, long y) => Math.Abs(x - y) < Tolerance,
        (string x, string y) => string.Equals(x, y, StringComparison.Ordinal),
        (IReadOnlyList<object?> x, IReadOnlyList<object?> y) => x.Count == y.Count && x.Zip(y).All(p => Equal(p.First, p.Second)),
        (IReadOnlyDictionary<string, object?> x, IReadOnlyDictionary<string, object?> y) =>
            x.Count == y.Count && x.All(p => y.TryGetValue(p.Key, out var other) && Equal(p.Value, other)),
        _ => Equals(a, b),
    };

    public static int Compare(object? a, object? b) => (a, b) switch
    {
        (string x, string y) => CodePoints.Compare(x, y),
        (bool x, bool y) => x.CompareTo(y),
        _ => AsDouble(a).CompareTo(AsDouble(b)),
    };

    public static string Format(object? value) => value switch
    {
        null => "null",
        string s => s,
        bool b => b ? "true" : "false",
        long l => l.ToString(CultureInfo.InvariantCulture),
        double d => DoubleText.Of(d),
        _ => value.ToString() ?? "",
    };
}
