namespace EtAlii.Adp.Specification.Cel;

/// <summary>CEL's <c>==</c> on two values, for a host that compares what expressions gave: lists and maps by their contents, an int and a double by their value.</summary>
public static class CelEquality
{
    public static bool Equal(object? a, object? b) => CelValues.Equal(a, b);
}
