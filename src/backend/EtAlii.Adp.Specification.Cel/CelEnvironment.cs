namespace EtAlii.Adp.Specification.Cel;

/// <summary>
/// What an expression may use: the variables it may read, the functions it may call (global and on a
/// receiver, each with its arity and cost), the comprehension macros, and the budget of one
/// evaluation. Compiling refuses anything else, naming it, so a specification that needs more fails at
/// load rather than at evaluation. A language builds its contexts as presets of this: FBL's three in
/// <c>EtAlii.Adp.Specification.Fbl</c>, DISL's in its runtime.
/// </summary>
public sealed class CelEnvironment
{
    /// <summary>The steps one evaluation may take unless an environment says otherwise.</summary>
    public const long DefaultBudget = 100_000;

    private readonly List<string> _variables = [];
    private readonly Dictionary<(string Name, CelCallStyle Style), CelFunction> _functions = [];
    private readonly Dictionary<string, CelMacro> _macros = new(StringComparer.Ordinal);

    /// <summary>An environment with nothing in it: no variables, functions or macros.</summary>
    public CelEnvironment()
    {
    }

    /// <summary>The variables an expression may read, in the order a refusal lists them.</summary>
    public IReadOnlyList<string> Variables => _variables;

    /// <summary>
    /// Whether an identifier that names no declared variable compiles anyway, failing only when it has no
    /// value at evaluation. For tools that parse expressions whose context they do not know.
    /// </summary>
    public bool AllowsUndeclaredVariables { get; set; }

    /// <summary>The cost one evaluation may spend.</summary>
    public long Budget { get; set; } = DefaultBudget;

    public IEnumerable<CelFunction> Functions => _functions.Values;

    public IEnumerable<CelMacro> Macros => _macros.Values;

    /// <summary>
    /// The CEL standard environment: the core functions and conversions, the strings, math, lists and
    /// optional libraries, and the macros <c>all</c>, <c>exists</c>, <c>exists_one</c>, <c>filter</c>,
    /// <c>map</c> and <c>sortBy</c>. No variables.
    /// </summary>
    public static CelEnvironment Standard()
    {
        var environment = new CelEnvironment();
        CelCore.Register(environment);
        CelStrings.Register(environment);
        CelMath.Register(environment);
        CelLists.Register(environment);
        CelOptionals.Register(environment);
        return environment;
    }

    /// <summary>A copy that can be extended without changing this one.</summary>
    public CelEnvironment Clone()
    {
        var copy = new CelEnvironment { AllowsUndeclaredVariables = AllowsUndeclaredVariables, Budget = Budget };
        copy._variables.AddRange(_variables);
        foreach (var pair in _functions) copy._functions[pair.Key] = pair.Value;
        foreach (var pair in _macros) copy._macros[pair.Key] = pair.Value;
        return copy;
    }

    public CelEnvironment DeclareVariable(string name)
    {
        if (!_variables.Contains(name)) _variables.Add(name);
        return this;
    }

    public CelEnvironment DeclareVariables(params IEnumerable<string> names)
    {
        foreach (var name in names) DeclareVariable(name);
        return this;
    }

    /// <summary>Adds <paramref name="function"/>, replacing one of the same name and call style.</summary>
    public CelEnvironment AddFunction(CelFunction function)
    {
        _functions[(function.Name, function.Style)] = function;
        return this;
    }

    /// <summary>Adds <paramref name="macro"/>, replacing one of the same name.</summary>
    public CelEnvironment AddMacro(CelMacro macro)
    {
        _macros[macro.Name] = macro;
        return this;
    }

    public bool TryGetFunction(string name, CelCallStyle style, out CelFunction function) =>
        _functions.TryGetValue((name, style), out function!);

    public bool TryGetMacro(string name, out CelMacro macro) => _macros.TryGetValue(name, out macro!);

    public bool IsVariable(string name) => _variables.Contains(name);

    /// <summary>Compiles <paramref name="expression"/>; a <see cref="CelException"/> names the first thing it cannot use.</summary>
    public CelProgram Compile(string expression)
    {
        var parser = new CelParser(expression, this);
        var node = parser.ParseExpression();
        parser.ExpectEnd();
        if (!AllowsUndeclaredVariables) CheckVariables(node, []);
        return new CelProgram(expression, node, Budget);
    }

    private void CheckVariables(CelNode node, HashSet<string> bound)
    {
        switch (node)
        {
            case CelNode.Ident ident:
                if (!_variables.Contains(ident.Name) && !bound.Contains(ident.Name))
                {
                    throw new CelException($"'{ident.Name}' is not a variable here; available: {string.Join(", ", _variables)}.");
                }
                break;
            case CelNode.Comprehension comprehension:
                CheckVariables(comprehension.Target, bound);
                CheckVariables(comprehension.Body, [.. bound, comprehension.Variable]);
                break;
            case CelNode.Bind bind:
                CheckVariables(bind.Init, bound);
                CheckVariables(bind.Body, [.. bound, bind.Variable]);
                break;
            default:
                foreach (var child in node.Children) CheckVariables(child, bound);
                break;
        }
    }
}
