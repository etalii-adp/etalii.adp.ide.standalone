namespace EtAlii.Adp.Specification.Cel;

/// <summary>CEL's optional value: what <c>x.?f</c>, <c>m[?k]</c>, <c>optional.of(v)</c> and <c>optional.none()</c> give.</summary>
public sealed class CelOptional : IEquatable<CelOptional>
{
    private readonly object? _value;

    private CelOptional(bool hasValue, object? value)
    {
        HasValue = hasValue;
        _value = value;
    }

    /// <summary><c>optional.none()</c>.</summary>
    public static CelOptional None { get; } = new(false, null);

    public bool HasValue { get; }

    /// <summary>The value; a <see cref="CelException"/> for <see cref="None"/>, as <c>value()</c> is an error there.</summary>
    public object? Value => HasValue ? _value : throw new CelException("optional.none() has no value.");

    /// <summary><c>optional.of(value)</c>.</summary>
    public static CelOptional Of(object? value) => new(true, value);

    public bool Equals(CelOptional? other) =>
        other is not null && HasValue == other.HasValue && (!HasValue || CelValues.Equal(_value, other._value));

    public override bool Equals(object? obj) => Equals(obj as CelOptional);

    public override int GetHashCode() => HasValue ? 1 : 0;

    public override string ToString() => HasValue ? $"optional.of({CelValues.Format(_value)})" : "optional.none()";
}
