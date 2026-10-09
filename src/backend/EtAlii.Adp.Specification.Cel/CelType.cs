namespace EtAlii.Adp.Specification.Cel;

/// <summary>
/// A CEL type as a value: what <c>type(x)</c> returns and what the identifiers <c>map</c>,
/// <c>list</c>, <c>string</c>, <c>int</c>, <c>double</c>, <c>bool</c> and <c>null_type</c> denote,
/// so that <c>type(entry.view) == map</c> asks what a value is before reading into it.
/// </summary>
public sealed record CelType(string Name)
{
    private static readonly Dictionary<string, CelType> _named = new[] { "map", "list", "string", "int", "double", "bool", "null_type", "type" }
        .ToDictionary(name => name, name => new CelType(name), StringComparer.Ordinal);

    /// <summary>The type an identifier names, when no variable has that name.</summary>
    public static bool TryNamed(string name, out CelType type) => _named.TryGetValue(name, out type!);

    /// <summary>The type of a value, as CEL names it.</summary>
    public static CelType Of(object? value) => _named[value switch
    {
        null => "null_type",
        string => "string",
        bool => "bool",
        long => "int",
        double => "double",
        CelType => "type",
        IReadOnlyDictionary<string, object?> => "map",
        IReadOnlyList<object?> => "list",
        _ => throw new CelException("type() does not know this value."),
    }];

    public override string ToString() => Name;
}
