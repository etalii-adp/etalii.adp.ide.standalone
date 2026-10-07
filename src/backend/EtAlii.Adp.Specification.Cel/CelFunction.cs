namespace EtAlii.Adp.Specification.Cel;

/// <summary>How a function is written at its call: <c>f(x, y)</c> or <c>x.f(y)</c>.</summary>
public enum CelCallStyle
{
    /// <summary><c>f(x, y)</c>, also with a dotted name such as <c>math.round(x)</c>.</summary>
    Global,

    /// <summary><c>x.f(y)</c>: the receiver is the first of the arguments the body sees.</summary>
    Receiver,
}

/// <summary>One call of a function: its arguments (a receiver first) and the budget it may charge further.</summary>
public sealed record CelCall(IReadOnlyList<object?> Arguments, CelBudget Budget)
{
    private static readonly IReadOnlyDictionary<string, object?> NoVariables = new Dictionary<string, object?>();

    /// <summary>
    /// The variables the evaluation the call is part of was given - not those a comprehension or
    /// <c>cel.bind</c> bound. A function that may read a context variable, as a DISL user function with
    /// <c>uses</c> may read <c>diagram</c>, takes it from here.
    /// </summary>
    public IReadOnlyDictionary<string, object?> Variables { get; init; } = NoVariables;

    public object? this[int index] => Arguments[index];

    public int Count => Arguments.Count;
}

/// <summary>
/// A function an environment offers: its name, how it is called, how many arguments it takes (the
/// receiver not counted), its body and its cost per call. A function without a body is a method only
/// <see cref="ICelObject"/>s implement: declaring it lets an expression call it, and calling it on any
/// other value is an evaluation error.
/// </summary>
public sealed class CelFunction
{
    /// <summary>For <see cref="MaxArguments"/>: any number.</summary>
    public const int Variadic = int.MaxValue;

    private readonly Func<IReadOnlyList<object?>, long>? _cost;

    public CelFunction(string name, CelCallStyle style, int minArguments, int maxArguments, Func<CelCall, object?>? body, Func<IReadOnlyList<object?>, long>? cost = null)
    {
        if (minArguments < 0 || maxArguments < minArguments) throw new ArgumentOutOfRangeException(nameof(maxArguments), "The arity range is empty.");
        Name = name;
        Style = style;
        MinArguments = minArguments;
        MaxArguments = maxArguments;
        Body = body;
        _cost = cost;
    }

    public string Name { get; }

    public CelCallStyle Style { get; }

    public int MinArguments { get; }

    public int MaxArguments { get; }

    public Func<CelCall, object?>? Body { get; }

    /// <summary><c>f(...)</c> with a fixed number of arguments.</summary>
    public static CelFunction Global(string name, int arguments, Func<IReadOnlyList<object?>, object?> body, Func<IReadOnlyList<object?>, long>? cost = null) =>
        new(name, CelCallStyle.Global, arguments, arguments, call => body(call.Arguments), cost);

    /// <summary><c>x.f(...)</c> with a fixed number of arguments besides the receiver, which the body sees first.</summary>
    public static CelFunction Receiver(string name, int arguments, Func<IReadOnlyList<object?>, object?> body, Func<IReadOnlyList<object?>, long>? cost = null) =>
        new(name, CelCallStyle.Receiver, arguments, arguments, call => body(call.Arguments), cost);

    /// <summary><c>x.f(...)</c> that only <see cref="ICelObject"/>s implement.</summary>
    public static CelFunction Method(string name, int minArguments, int maxArguments) =>
        new(name, CelCallStyle.Receiver, minArguments, maxArguments, null);

    /// <summary>The cost one call charges, at least one step; library functions over lists and strings charge their length.</summary>
    public long CostOf(IReadOnlyList<object?> arguments) => Math.Max(1, _cost?.Invoke(arguments) ?? 1);

    internal string Arity => MinArguments == MaxArguments
        ? MinArguments.ToString(System.Globalization.CultureInfo.InvariantCulture)
        : MaxArguments == Variadic
            ? $"at least {MinArguments}"
            : $"{MinArguments} to {MaxArguments}";
}
