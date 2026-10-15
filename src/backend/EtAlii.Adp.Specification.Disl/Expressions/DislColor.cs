using System.Globalization;
using EtAlii.Adp.Specification.Cel;

namespace EtAlii.Adp.Specification.Disl;

/// <summary>
/// The colour of <c>color(c)</c> (DISL §12.4): a hex colour taken apart into its sRGB channels and
/// its alpha, each from 0 to 1, which <c>mix</c> manipulates and writes back as <c>#rrggbbaa</c>.
/// </summary>
/// <remarks>
/// Only the hex forms are read (<c>#rgb</c>, <c>#rgba</c>, <c>#rrggbb</c>, <c>#rrggbbaa</c>); any
/// other colour is an evaluation error. Of the colour's methods only <c>mix</c> is implemented, the
/// one a bundled definition calls; <c>lighten</c>, <c>darken</c>, <c>alpha</c> and
/// <c>contrastText</c> are refused at load, naming them.
/// </remarks>
internal sealed record DislColor(double Red, double Green, double Blue, double Alpha)
{
    /// <summary><c>color(c)</c>: <paramref name="value"/> read as a hex colour.</summary>
    public static DislColor Of(object? value)
    {
        var text = value as string ?? throw new CelException("color() takes a colour string.");
        var hex = text.StartsWith('#') ? text[1..] : throw new CelException($"color() takes a hex colour, not '{text}'.");
        if (hex.Length is 3 or 4) hex = string.Concat(hex.Select(digit => new string(digit, 2)));
        if (hex.Length is not (6 or 8) || !uint.TryParse(hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out _))
        {
            throw new CelException($"color() takes a hex colour, not '{text}'.");
        }
        double Channel(int at) => int.Parse(hex.AsSpan(at, 2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture) / 255.0;
        return new DislColor(Channel(0), Channel(2), Channel(4), hex.Length == 8 ? Channel(6) : 1.0);
    }

    /// <summary>
    /// <c>color(c).mix(c2, f)</c>: CSS Color 5 <c>color-mix(in srgb, c (1 − f) × 100%, c2 f × 100%)</c>,
    /// interpolated on gamma-encoded sRGB with premultiplied alpha and written as <c>#rrggbbaa</c>.
    /// </summary>
    public static string Mix(object? receiver, object? other, object? fraction)
    {
        var first = receiver as DislColor ?? throw new CelException("mix() is a method of color().");
        var second = Of(other);
        var f = fraction switch
        {
            long integer => integer,
            double real => real,
            _ => throw new CelException("mix() takes a fraction from 0 to 1."),
        };
        if (f is < 0 or > 1 || double.IsNaN(f)) throw new CelException($"mix() takes a fraction from 0 to 1, not {f.ToString(CultureInfo.InvariantCulture)}.");

        var alpha = (first.Alpha * (1 - f)) + (second.Alpha * f);
        double Channel(double one, double two) => alpha == 0 ? 0 : ((one * first.Alpha * (1 - f)) + (two * second.Alpha * f)) / alpha;
        return Hex(Channel(first.Red, second.Red), Channel(first.Green, second.Green), Channel(first.Blue, second.Blue), alpha);
    }

    private static string Hex(params double[] channels) =>
        "#" + string.Concat(channels.Select(channel => ((int)Math.Round(Math.Clamp(channel, 0, 1) * 255, MidpointRounding.AwayFromZero)).ToString("x2", CultureInfo.InvariantCulture)));
}
