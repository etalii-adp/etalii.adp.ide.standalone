namespace EtAlii.Adp.Specification.Cel;

/// <summary>
/// CEL's optional values: <c>optional.of(v)</c>, <c>optional.none()</c>, and on an optional
/// <c>hasValue()</c>, <c>value()</c>, <c>orValue(v)</c> and <c>or(o)</c>. The syntax <c>x.?f</c> and
/// <c>m[?k]</c> is the parser's.
/// </summary>
public static class CelOptionals
{
    public static void Register(CelEnvironment environment)
    {
        environment.AddFunction(CelFunction.Global("optional.of", 1, a => CelOptional.Of(a[0])));
        environment.AddFunction(CelFunction.Global("optional.none", 0, _ => CelOptional.None));
        environment.AddFunction(CelFunction.Receiver("hasValue", 0, a => Optional(a[0]).HasValue));
        environment.AddFunction(CelFunction.Receiver("value", 0, a => Optional(a[0]).Value));
        environment.AddFunction(CelFunction.Receiver("orValue", 1, a => Optional(a[0]) is { HasValue: true } o ? o.Value : a[1]));
        environment.AddFunction(CelFunction.Receiver("or", 1, a => Optional(a[0]) is { HasValue: true } o ? o : Optional(a[1])));
    }

    private static CelOptional Optional(object? value) => value as CelOptional ?? throw new CelException("An optional value was expected.");
}
