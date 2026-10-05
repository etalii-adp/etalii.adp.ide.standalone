namespace EtAlii.Adp.Specification.Cel;

/// <summary>CEL's string functions, called on a string receiver.</summary>
public static class CelStrings
{
    public static void Register(CelEnvironment environment)
    {
        environment.AddFunction(CelFunction.Receiver("startsWith", 1, a => Text(a[0]).StartsWith(Text(a[1]), StringComparison.Ordinal), CelCore.Length));
        environment.AddFunction(CelFunction.Receiver("endsWith", 1, a => Text(a[0]).EndsWith(Text(a[1]), StringComparison.Ordinal), CelCore.Length));
        environment.AddFunction(CelFunction.Receiver("contains", 1, a => Text(a[0]).Contains(Text(a[1]), StringComparison.Ordinal), CelCore.Length));
        environment.AddFunction(CelFunction.Receiver("replace", 2, a => Text(a[0]).Replace(Text(a[1]), Text(a[2]), StringComparison.Ordinal), CelCore.Length));
        environment.AddFunction(CelFunction.Receiver("lowerAscii", 0, a => Text(a[0]).ToLowerInvariant(), CelCore.Length));
        environment.AddFunction(CelFunction.Receiver("upperAscii", 0, a => Text(a[0]).ToUpperInvariant(), CelCore.Length));
    }

    private static string Text(object? value) => CelValues.AsString(value);
}
