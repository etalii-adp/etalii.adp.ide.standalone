using System.Globalization;
using System.Text;

namespace EtAlii.Adp.Specification.Cel;

/// <summary>
/// <c>string(double)</c> as CEL's reference implementation writes it, Go's <c>%g</c>: the shortest
/// digits that read back as the same double, in decimal notation for exponents from -4 to 5 and in
/// <c>d.ddde±XX</c> notation outside them. So <c>1.0</c> is <c>"1"</c>, <c>0.25</c> is <c>"0.25"</c>,
/// <c>1e6</c> is <c>"1e+06"</c> and <c>0.00001</c> is <c>"1e-05"</c>; NaN and the infinities are
/// <c>"NaN"</c>, <c>"+Inf"</c> and <c>"-Inf"</c>.
/// </summary>
internal static class DoubleText
{
    public static string Of(double value)
    {
        if (double.IsNaN(value)) return "NaN";
        if (double.IsPositiveInfinity(value)) return "+Inf";
        if (double.IsNegativeInfinity(value)) return "-Inf";
        if (value == 0) return double.IsNegative(value) ? "-0" : "0";

        // .NET's "R" gives the shortest round-tripping digits, as Go's shortest formatting does; only the layout differs.
        (string digits, int point) = Digits(Math.Abs(value).ToString("R", CultureInfo.InvariantCulture));
        var text = new StringBuilder();
        if (value < 0) text.Append('-');
        var exponent = point - 1;
        if (exponent < -4 || exponent >= 6)
        {
            text.Append(digits[0]);
            if (digits.Length > 1) text.Append('.').Append(digits, 1, digits.Length - 1);
            text.Append('e').Append(exponent < 0 ? '-' : '+').Append(Math.Abs(exponent).ToString("00", CultureInfo.InvariantCulture));
        }
        else if (point <= 0)
        {
            text.Append("0.").Append('0', -point).Append(digits);
        }
        else if (point >= digits.Length)
        {
            text.Append(digits).Append('0', point - digits.Length);
        }
        else
        {
            text.Append(digits, 0, point).Append('.').Append(digits, point, digits.Length - point);
        }
        return text.ToString();
    }

    /// <summary>The significant digits and the decimal point's position: the value is 0.<i>digits</i> × 10^<i>point</i>.</summary>
    private static (string Digits, int Point) Digits(string shortest)
    {
        var exponent = 0;
        var mantissa = shortest;
        var e = shortest.IndexOfAny(['E', 'e']);
        if (e >= 0)
        {
            exponent = int.Parse(shortest[(e + 1)..], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
            mantissa = shortest[..e];
        }
        var dot = mantissa.IndexOf('.');
        var whole = dot < 0 ? mantissa : mantissa[..dot];
        var digits = whole + (dot < 0 ? "" : mantissa[(dot + 1)..]);
        var point = whole.Length + exponent;
        var leading = 0;
        while (leading < digits.Length - 1 && digits[leading] == '0') leading++;
        digits = digits[leading..].TrimEnd('0');
        return (digits.Length == 0 ? "0" : digits, point - leading);
    }
}
