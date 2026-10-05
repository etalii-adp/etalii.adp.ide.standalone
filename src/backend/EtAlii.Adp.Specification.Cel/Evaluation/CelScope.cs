namespace EtAlii.Adp.Specification.Cel;

internal sealed class CelScope
{
    private readonly IReadOnlyDictionary<string, object?> _variables;
    private readonly CelScope? _parent;
    private readonly string? _name;
    private readonly object? _value;

    public CelScope(IReadOnlyDictionary<string, object?> variables, CelBudget budget)
    {
        _variables = variables;
        Budget = budget;
    }

    private CelScope(CelScope parent, string name, object? value)
    {
        _variables = parent._variables;
        Budget = parent.Budget;
        _parent = parent;
        _name = name;
        _value = value;
    }

    public CelBudget Budget { get; }

    public CelScope With(string name, object? value) => new(this, name, value);

    public object? Lookup(string name)
    {
        for (var s = this; s is not null; s = s._parent)
        {
            if (s._name == name) return s._value;
        }
        if (_variables.TryGetValue(name, out var value)) return value;
        throw new CelException($"'{name}' has no value.");
    }

    public void Step() => Budget.Charge(1);
}
