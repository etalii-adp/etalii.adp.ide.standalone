using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace EtAlii.Adp;

/// <summary>
/// A <see cref="Guid"/> represented as a fixed-length, 25-character base36 string
/// (25 is the smallest width in which every possible 128-bit value fits: 36^24 &lt; 2^128 &#le; 36^25).
/// </summary>
public readonly record struct ShortGuid :
    IComparable<ShortGuid>,
    ISpanFormattable,
    ISpanParsable<ShortGuid>
{
    public const int Length = 25;
    private const string Alphabet = "0123456789abcdefghijklmnopqrstuvwxyz";

    public static readonly ShortGuid Empty = default;

    private readonly Guid _value;

    public ShortGuid(Guid value) => _value = value;

    public Guid Guid => _value;

    public static ShortGuid NewShortGuid() => new(Guid.NewGuid());

    /// <summary>
    /// Deterministically derives a ShortGuid from a name, so the same name always
    /// yields the same identity (e.g. a stable per-username id with no separate
    /// persisted name-to-id mapping needed).
    /// </summary>
    public static ShortGuid FromName(string name)
    {
        return new ShortGuid(new Guid(MD5.HashData(Encoding.UTF8.GetBytes(name))));
    }

    public static implicit operator ShortGuid(Guid value) => new(value);
    public static implicit operator Guid(ShortGuid value) => value._value;

    public bool Equals(ShortGuid other) => _value.Equals(other._value);
    public override int GetHashCode() => _value.GetHashCode();

    public int CompareTo(ShortGuid other) => ToUInt128().CompareTo(other.ToUInt128());

    public static bool operator <(ShortGuid left, ShortGuid right) => left.CompareTo(right) < 0;
    public static bool operator <=(ShortGuid left, ShortGuid right) => left.CompareTo(right) <= 0;
    public static bool operator >(ShortGuid left, ShortGuid right) => left.CompareTo(right) > 0;
    public static bool operator >=(ShortGuid left, ShortGuid right) => left.CompareTo(right) >= 0;

    private UInt128 ToUInt128()
    {
        Span<byte> bytes = stackalloc byte[16];
        _value.TryWriteBytes(bytes, bigEndian: true, out _);
        return BinaryPrimitives.ReadUInt128BigEndian(bytes);
    }

    private static Guid FromUInt128(UInt128 value)
    {
        Span<byte> bytes = stackalloc byte[16];
        BinaryPrimitives.WriteUInt128BigEndian(bytes, value);
        return new Guid(bytes, bigEndian: true);
    }

    public override string ToString() => string.Create(Length, this, static (destination, state) => state.TryFormat(destination, out _));

    public string ToString(string? format, IFormatProvider? formatProvider) => ToString();

    public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format = default, IFormatProvider? provider = null)
    {
        if (destination.Length < Length)
        {
            charsWritten = 0;
            return false;
        }

        var value = ToUInt128();
        for (var i = Length - 1; i >= 0; i--)
        {
            destination[i] = Alphabet[(int)(value % 36)];
            value /= 36;
        }

        charsWritten = Length;
        return true;
    }

    public static ShortGuid Parse(string s, IFormatProvider? provider = null) => Parse(s.AsSpan(), provider);

    public static bool TryParse(string? s, IFormatProvider? provider, out ShortGuid result) =>
        TryParse(s.AsSpan(), provider, out result);

    public static ShortGuid Parse(ReadOnlySpan<char> s, IFormatProvider? provider = null) =>
        TryParse(s, provider, out var result) ? result : throw new FormatException($"'{s}' is not a valid {nameof(ShortGuid)}.");

    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out ShortGuid result)
    {
        result = default;

        if (s.Length != Length)
        {
            return false;
        }

        UInt128 value = 0;
        try
        {
            foreach (var c in s)
            {
                int digit;
                switch (c)
                {
                    case >= '0' and <= '9': digit = c - '0'; break;
                    case >= 'a' and <= 'z': digit = c - 'a' + 10; break;
                    case >= 'A' and <= 'Z': digit = c - 'A' + 10; break;
                    default: return false;
                }

                value = checked(value * 36 + (UInt128)digit);
            }

            result = new ShortGuid(FromUInt128(value));
            return true;
        }
        catch (OverflowException)
        {
            return false;
        }
    }
}
