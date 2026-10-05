namespace EtAlii.Adp.Specification.Cel;

internal sealed class CelScope(IReadOnlyDictionary<string, object?> variables, CelScope? parent)
{
    private readonly Dictionary<string, object?> _locals = new(StringComparer.Ordinal);

    public CelScope With(string name, object? value)
    {
        var scope = new CelScope(variables, this);
        scope._locals[name] = value;
        return scope;
    }

    public object? Lookup(string name)
    {
        for (var s = this; s is not null; s = s.Parent)
        {
            if (s._locals.TryGetValue(name, out var local)) return local;
        }
        if (variables.TryGetValue(name, out var value)) return value;
        throw new CelException($"'{name}' has no value.");
    }

    private CelScope? Parent => parent;
}
