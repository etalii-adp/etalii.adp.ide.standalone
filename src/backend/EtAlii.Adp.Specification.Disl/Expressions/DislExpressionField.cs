using EtAlii.Adp.Specification.Cel;

namespace EtAlii.Adp.Specification.Disl;

/// <summary>
/// An Expression position (DISL §2.5 a) typed at load: where it is, the CEL it holds - a bare string, or the
/// <c>cel</c> of an object - and the program the loader compiled for it.
/// </summary>
/// <param name="Pointer">The position, as a refusal names it.</param>
/// <param name="Source">The CEL, or null when the position holds no expression: a number, a list, an object without a <c>cel</c> string.</param>
/// <param name="Program">
/// The program the loader compiled for the source, from the specification's cache; null for a position the loader does
/// not walk (inside an <c>x-</c> key), which is compiled on first use as <see cref="DislEvaluation"/> compiles it.
/// </param>
internal sealed record DislExpressionField(string Pointer, string? Source, CelProgram? Program)
{
    /// <summary>The value in <paramref name="context"/>: a <see cref="CelError"/> when the position holds no expression or it fails.</summary>
    public object? Evaluate(DislSpecification specification, DislContext context, IReadOnlyDictionary<string, object?> variables) =>
        DislEvaluation.Evaluate(specification, Pointer, Source, Program, context, variables);

    /// <summary>Whether the condition holds; a condition that fails does not.</summary>
    public bool Holds(DislSpecification specification, DislContext context, IReadOnlyDictionary<string, object?> variables) =>
        Evaluate(specification, context, variables) is true;
}

/// <summary>
/// A value position of an action (DISL §2.5 d), typed at load: an Expression when the definition writes a string or
/// an object, else the value it writes.
/// </summary>
internal abstract record DislActionValue(string Pointer)
{
    /// <summary>What the position gives: the expression's value (a <see cref="CelError"/> when it fails), or the literal.</summary>
    public abstract object? Evaluate(DislSpecification specification, DislContext context, IReadOnlyDictionary<string, object?> variables);

    /// <summary>A string or an object: an Expression.</summary>
    public sealed record Expression(DislExpressionField Field) : DislActionValue(Field.Pointer)
    {
        public override object? Evaluate(DislSpecification specification, DislContext context, IReadOnlyDictionary<string, object?> variables) =>
            Field.Evaluate(specification, context, variables);
    }

    /// <summary>
    /// A value written as it is - in an action a number, a boolean, null or a list; in an operation's plugin <c>args</c>
    /// any value - as the value it reads as. Each evaluation gives a copy of its own, as a reading of the JSON did, so a
    /// run that keeps a list cannot change the next run's.
    /// </summary>
    public sealed record Literal(string Pointer, object? Value) : DislActionValue(Pointer)
    {
        public override object? Evaluate(DislSpecification specification, DislContext context, IReadOnlyDictionary<string, object?> variables) => Copy(Value);

        private static object? Copy(object? value) => value switch
        {
            List<object?> list => list.Select(Copy).ToList(),
            CelMap map => map.Aggregate(new CelMap(), (copy, entry) =>
            {
                copy[entry.Key] = Copy(entry.Value);
                return copy;
            }),
            _ => value,
        };
    }
}
