namespace EtAlii.Adp.Specification.Cel;

internal sealed class CelScope
{
    private readonly CelScope? _parent;
    private readonly string? _name;
    private readonly object? _value;

    public CelScope(IReadOnlyDictionary<string, object?> variables, CelBudget budget)
    {
        Variables = variables;
        Budget = budget;
    }

    private CelScope(CelScope parent, string name, object? value)
    {
        Variables = parent.Variables;
        Budget = parent.Budget;
        _parent = parent;
        _name = name;
        _value = value;
    }

    public CelBudget Budget { get; }

    /// <summary>The variables the evaluation was given, without what comprehensions and <c>cel.bind</c> bound.</summary>
    public IReadOnlyDictionary<string, object?> Variables { get; }

    public CelScope With(string name, object? value) => new(this, name, value);

    public object? Lookup(string name)
    {
        for (var s = this; s is not null; s = s._parent)
        {
            if (s._name == name) return s._value;
        }
        if (Variables.TryGetValue(name, out var value)) return value;
        // A type's name denotes the type only where nothing else has the name.
        if (CelType.TryNamed(name, out var type)) return type;
        throw new CelException($"'{name}' has no value.");
    }

    public void Step() => Budget.Charge(1);
}
