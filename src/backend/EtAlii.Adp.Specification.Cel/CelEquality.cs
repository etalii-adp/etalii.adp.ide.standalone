namespace EtAlii.Adp.Specification.Cel;

/// <summary>CEL's <c>==</c> on two values, for a host that compares what expressions gave: lists and maps by their contents, an int and a double by their value.</summary>
public static class CelEquality
{
    public static bool Equal(object? a, object? b) => CelValues.Equal(a, b);
}

/// <summary>
/// CEL's <c>&lt;</c> on two scalars, for a host that sorts what expressions gave: strings by Unicode code point, false
/// before true, and numbers by their value whatever their type.
/// </summary>
public static class CelOrdering
{
    public static int Compare(object? a, object? b) => CelValues.Compare(a, b);
}
