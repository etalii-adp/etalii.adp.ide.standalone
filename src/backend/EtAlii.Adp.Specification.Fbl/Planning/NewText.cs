using System.Globalization;
using System.Text;
using EtAlii.Adp.Specification.Fbl.Documents;

namespace EtAlii.Adp.Specification.Fbl.Planning;

/// <summary>
/// The rules of FBL §6.3 that are the same for every family: numbers, times, maps and the
/// <c>emit</c> templates of new entries.
/// </summary>
internal static class NewText
{
    /// <summary>
    /// A number as FBL §6.3 writes it: with <c>shortest</c>, an integer without a fraction and any
    /// other number in ECMAScript's <c>Number.prototype.toString</c> form; with <c>{decimals: n}</c>,
    /// rounded halves away from zero, trailing zeros and a trailing point dropped.
    /// </summary>
    public static string Number(double value, int? decimals)
    {
        if (decimals is { } n)
        {
            var rounded = Math.Round(value, n, MidpointRounding.AwayFromZero);
            var text = rounded.ToString("F" + n.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
            if (text.Contains('.')) text = text.TrimEnd('0').TrimEnd('.');
            return text == "-0" ? "0" : text;
        }
        if (double.IsNaN(value) || double.IsInfinity(value)) throw new ArgumentOutOfRangeException(nameof(value), "A number in a body is finite.");
        if (value == 0) return "0";
        if (Math.Abs(Math.Floor(value) - value) < double.Tolerance && Math.Abs(value) < 1e21) return ((decimal)value).ToString(CultureInfo.InvariantCulture);
        var shortest = value.ToString("R", CultureInfo.InvariantCulture);
        var exponent = shortest.IndexOf('E');
        if (exponent < 0) return shortest;
        // ECMAScript writes 1e+21 and 1e-7 where .NET writes 1E+21 and 1E-07.
        var mantissa = shortest[..exponent];
        var power = int.Parse(shortest[(exponent + 1)..], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
        if (power is >= -6 and < 21)
        {
            return decimal.Parse(shortest, NumberStyles.Float, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);
        }
        return $"{mantissa}e{(power < 0 ? "-" : "+")}{Math.Abs(power).ToString(CultureInfo.InvariantCulture)}";
    }

    public static bool TryNumber(object? value, out double number)
    {
        switch (value)
        {
            case long l: number = l; return true;
            case int i: number = i; return true;
            case double d: number = d; return true;
            case decimal m: number = (double)m; return true;
            default: number = 0; return false;
        }
    }

    /// <summary>A value's written form for families without typed scalars: numbers by the rule above, booleans in lower case.</summary>
    public static string Plain(object? value, AttributeBinding? binding) => value switch
    {
        null => "",
        string s => s,
        bool b => b ? "true" : "false",
        _ when TryNumber(value, out var n) => Number(n, binding?.Decimals),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "",
    };

    public static bool IsEmpty(object? value) => value switch
    {
        null => true,
        string s => s.Length == 0,
        System.Collections.ICollection c => c.Count == 0,
        _ => false,
    };

    /// <summary>
    /// The wire value for a model value through a <c>map</c> (FBL §5.2): the first key in document
    /// order whose value matches, unless the value being replaced already maps to it.
    /// </summary>
    public static string? Wire(AttributeBinding binding, object? value, string? replaced)
    {
        if (binding.Map is not { } map) return null;
        var model = Plain(value, binding);
        if (replaced is not null && map.Any(m => m.Key == replaced && m.Value == model)) return replaced;
        foreach ((string wire, string mapped) in map)
        {
            if (mapped == model) return wire;
        }
        return null;
    }

    /// <summary>
    /// A date or date-time written with the precision of the value it replaces (FBL §6.3,
    /// <c>time: "keep-precision"</c>); null when either is not a date or date-time.
    /// </summary>
    public static string? KeepPrecision(string replaced, string value)
    {
        if (!TryParseTime(value, out var time, out _)) return null;
        if (!TryParseTime(replaced, out _, out var shape)) return null;
        if (!shape.HasTime) return time.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var builder = new StringBuilder(time.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture));
        if (shape.FractionDigits > 0)
        {
            var fraction = time.ToString("fffffff", CultureInfo.InvariantCulture)[..Math.Min(7, shape.FractionDigits)];
            builder.Append('.').Append(fraction);
        }
        if (shape.Offset is { } offset)
        {
            if (offset == "Z") builder.Append('Z');
            else builder.Append(time.ToString("zzz", CultureInfo.InvariantCulture));
        }
        return builder.ToString();
    }

    private readonly record struct TimeShape(bool HasTime, int FractionDigits, string? Offset);

    private static bool TryParseTime(string text, out DateTimeOffset time, out TimeShape shape)
    {
        shape = default;
        time = default;
        if (text.Length < 10 || text[4] != '-' || text[7] != '-') return false;
        if (text.Length == 10)
        {
            if (!DateTime.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) return false;
            time = new DateTimeOffset(date, TimeSpan.Zero);
            shape = new TimeShape(false, 0, null);
            return true;
        }
        if (text[10] is not ('T' or 't' or ' ')) return false;
        var rest = text[19..];
        var fraction = 0;
        if (rest.StartsWith('.'))
        {
            fraction = 1;
            while (fraction < rest.Length && char.IsAsciiDigit(rest[fraction])) fraction++;
            fraction--;
        }
        var offsetText = rest[(fraction > 0 ? fraction + 1 : 0)..];
        string? offset = offsetText.Length == 0 ? null : offsetText.Trim();
        if (!DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, offset is null ? DateTimeStyles.AssumeUniversal : DateTimeStyles.None, out time)) return false;
        if (offset is null) time = new DateTimeOffset(time.DateTime, TimeSpan.Zero);
        shape = new TimeShape(true, fraction, offset is "Z" or "z" ? "Z" : offset);
        return true;
    }

    /// <summary>
    /// Renders an <c>emit</c> template (FBL §6.2): placeholders <c>{name}</c> replaced by
    /// <paramref name="value"/>; a <c>[ … ]</c> segment written only when every placeholder in it
    /// is non-empty. Any other <c>{</c> or <c>}</c> is literal.
    /// </summary>
    public static string Render(string template, Func<string, string?> value)
    {
        var output = new StringBuilder();
        var i = 0;
        while (i < template.Length)
        {
            var c = template[i];
            if (c == '[')
            {
                var close = template.IndexOf(']', i + 1);
                if (close > 0)
                {
                    var segment = template[(i + 1)..close];
                    var complete = true;
                    var rendered = RenderPart(segment, name =>
                    {
                        var v = value(name);
                        if (string.IsNullOrEmpty(v)) complete = false;
                        return v;
                    });
                    if (complete) output.Append(rendered);
                    i = close + 1;
                    continue;
                }
            }
            var next = template.IndexOf('[', i);
            var end = next < 0 ? template.Length : next;
            output.Append(RenderPart(template[i..end], value));
            i = end;
            if (next >= 0 && template.IndexOf(']', next + 1) < 0)
            {
                output.Append('[');
                i++;
            }
        }
        return output.ToString();
    }

    private static string RenderPart(string part, Func<string, string?> value)
    {
        var output = new StringBuilder();
        var i = 0;
        while (i < part.Length)
        {
            if (part[i] == '{')
            {
                var close = part.IndexOf('}', i + 1);
                if (close > i + 1 && IsPlaceholderName(part[(i + 1)..close]))
                {
                    output.Append(value(part[(i + 1)..close]) ?? "");
                    i = close + 1;
                    continue;
                }
            }
            output.Append(part[i]);
            i++;
        }
        return output.ToString();
    }

    private static bool IsPlaceholderName(string name) =>
        name.Length > 0 && name.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '_' or ':' or '-');

    /// <summary>The placeholders of a template in order, each with whether it sits inside an optional segment.</summary>
    public static IReadOnlyList<EmitPart> Parts(string template)
    {
        var parts = new List<EmitPart>();
        var literal = new StringBuilder();
        var segment = -1;
        var segments = 0;
        for (var i = 0; i < template.Length; i++)
        {
            var c = template[i];
            if (c == '[' && segment < 0 && template.IndexOf(']', i + 1) > 0)
            {
                Flush();
                segment = segments++;
                continue;
            }
            if (c == ']' && segment >= 0)
            {
                Flush();
                segment = -1;
                continue;
            }
            if (c == '{')
            {
                var close = template.IndexOf('}', i + 1);
                if (close > i + 1 && IsPlaceholderName(template[(i + 1)..close]))
                {
                    Flush();
                    parts.Add(new EmitPart(template[(i + 1)..close], null, segment));
                    i = close;
                    continue;
                }
            }
            literal.Append(c);
        }
        Flush();
        return parts;

        void Flush()
        {
            if (literal.Length == 0) return;
            parts.Add(new EmitPart(null, literal.ToString(), segment));
            literal.Clear();
        }
    }
}

/// <summary>One part of an emit template: a placeholder or a literal, and the optional segment it is in (-1 for none).</summary>
internal sealed record EmitPart(string? Placeholder, string? Literal, int Segment);
