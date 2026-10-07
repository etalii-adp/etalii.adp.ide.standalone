namespace EtAlii.Adp.Specification.Cel.Tests;

/// <summary>
/// A host value for tests: named fields, where a field set to <see cref="Absent"/> exists for selection
/// but not for <c>has()</c>, and two methods - <c>greet(name)</c> and <c>isA(type)</c>.
/// </summary>
internal sealed class HostObject(string type, IReadOnlyDictionary<string, object?> fields) : ICelObject
{
    public static readonly object Absent = new();

    public string Type { get; } = type;

    public bool TryGetMember(string name, out object? value)
    {
        if (name == "type")
        {
            value = Type;
            return true;
        }
        if (fields.TryGetValue(name, out value))
        {
            if (value == Absent) value = null;
            return true;
        }
        return false;
    }

    public bool HasMember(string name) => name == "type" || (fields.TryGetValue(name, out var value) && value != Absent);

    public bool TryInvoke(string name, IReadOnlyList<object?> arguments, out object? value)
    {
        switch (name)
        {
            case "greet":
                value = "hello " + (string?)arguments[0] + " from " + Type;
                return true;
            case "isA":
                value = (string?)arguments[0] == Type;
                return true;
            default:
                value = null;
                return false;
        }
    }

    public override string ToString() => Type;
}
