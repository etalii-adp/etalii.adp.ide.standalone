namespace EtAlii.Adp.Specification.Cel;

/// <summary>
/// A comprehension macro, written <c>target.name(x, expression)</c>: <paramref name="Expand"/> gets the
/// target's items (a map's keys) and a function that evaluates the expression with <c>x</c> bound to
/// one item. <c>all</c>, <c>exists</c>, <c>exists_one</c>, <c>filter</c> and <c>map</c> are of this
/// shape; <c>has()</c> and <c>cel.bind()</c> are part of the language instead.
/// </summary>
public sealed record CelMacro(string Name, Func<IReadOnlyList<object?>, Func<object?, object?>, object?> Expand);
