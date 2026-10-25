using System.Globalization;
using System.Text.RegularExpressions;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Specification.Cel;
using EtAlii.Adp.Specification.Disl;

namespace EtAlii.Adp.Diagram.WardleyMap;

/// <summary>
/// The CEL functions of the definition's <c>net.etalii.adp.wardley.functions</c> plugin (DISL §13.1.1): what the
/// definition needs and DISL's CEL cannot do, answered from the module's own reading.
/// </summary>
/// <remarks>
/// <para>
/// <b><c>writtenEnd</c></b> is the name a link's <c>source</c> or <c>target</c>, or an evolution's or a pipeline's
/// <c>component</c>, is written with in the map. <see cref="WardleyDisl"/> keeps it beside the model as a host
/// attribute, because an end that names no element is null in the model and CEL could not otherwise say what it named.
/// </para>
/// <para>
/// <b><c>fixedNumber</c></b> writes a number as the format writes it and every row and sentence of the tool shows it,
/// <see cref="DslNumber.Format(double)"/>: at most four decimals, none when whole, in the invariant culture whatever the
/// user's locale. It stands in for gap 10, DISL's <c>formatNumber</c> following <c>env.locale</c>.
/// </para>
/// <para>
/// <b><c>writtenDecorators</c></b> is an element's decorators in the order the map writes them, which the Decorators
/// row shows; the five booleans of the model have no order. It stands in for gap 5, a set of words bound as one value.
/// </para>
/// <para>
/// <b><c>positions</c></b> gives an annotation's occurrences, each as <c>[visibility, maturity]</c>, from the text
/// <see cref="WardleyDisl"/> writes for them (<see cref="PositionsText"/>). It stands in for gap 6, a list of structured
/// values inside one statement.
/// </para>
/// </remarks>
internal static partial class WardleyFunctions
{
    /// <summary>The host attribute holding a link's <c>source</c> as written.</summary>
    public const string WrittenSource = "writtenSource";

    /// <summary>The host attribute holding a link's <c>target</c> as written.</summary>
    public const string WrittenTarget = "writtenTarget";

    /// <summary>The host attribute holding an evolution's or a pipeline's <c>component</c> as written.</summary>
    public const string WrittenComponent = "writtenComponent";

    /// <summary>The host attribute holding an element's decorators as written, in their order, joined by <c>, </c>.</summary>
    public const string WrittenDecorators = "writtenDecorators";

    /// <summary>The implementations, for <see cref="BundledDefinition.Load(System.Reflection.Assembly, string, DislPluginFunctions)"/>.</summary>
    public static DislPluginFunctions Plugins() => new DislPluginFunctions()
        .Add("writtenEnd", arguments => WrittenEnd(arguments[0], arguments[1]))
        .Add("fixedNumber", arguments => FixedNumber(arguments[0]))
        .Add("positions", arguments => Positions(arguments[0] as string ?? throw new CelException("positions() takes text.")))
        .Add("writtenDecorators", arguments => WrittenDecoratorsOf(arguments[0]));

    /// <summary>The decorators <paramref name="element"/> is written with, in the order they are written, joined by <c>, </c>.</summary>
    public static string WrittenDecoratorsOf(object? element) =>
        element is DislElement written
            ? written.HostAttributes.TryGetValue(WrittenDecorators, out var decorators) ? decorators as string ?? "" : ""
            : throw new CelException("writtenDecorators() takes an element of this map.");

    /// <summary>The name <paramref name="element"/>'s end <paramref name="end"/> is written with; empty when nothing is written.</summary>
    public static string WrittenEnd(object? element, object? end)
    {
        var host = end switch
        {
            "source" => WrittenSource,
            "target" => WrittenTarget,
            "component" => WrittenComponent,
            _ => throw new CelException("writtenEnd() takes 'source', 'target' or 'component'."),
        };
        return element is DislElement written
            ? written.HostAttributes.TryGetValue(host, out var name) ? name as string ?? "" : ""
            : throw new CelException("writtenEnd() takes an element of this map.");
    }

    /// <summary><paramref name="value"/> as the format writes a number.</summary>
    public static string FixedNumber(object? value) => value switch
    {
        double number => DslNumber.Format(number),
        long whole => DslNumber.Format(whole),
        _ => throw new CelException("fixedNumber() takes a number."),
    };

    /// <summary>The occurrences <paramref name="text"/> names, each as <c>[visibility, maturity]</c>.</summary>
    public static List<object?> Positions(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var pairs = text.Contains('[', StringComparison.Ordinal)
            ? Bracketed().Matches(text).Select(match => match.Groups["values"].Value)
            : [text];
        return [.. pairs.Select(Pair)];
    }

    /// <summary>The text <see cref="Positions"/> reads <paramref name="occurrences"/> back from: each pair in brackets, at full precision.</summary>
    public static string PositionsText(IEnumerable<WardleyCoordinate> occurrences)
    {
        ArgumentNullException.ThrowIfNull(occurrences);
        return string.Join(",", occurrences.Select(occurrence => string.Create(CultureInfo.InvariantCulture, $"[{occurrence.Visibility:R},{occurrence.Maturity:R}]")));
    }

    private static List<object?> Pair(string values)
    {
        var numbers = values.Split(',');
        return numbers.Length == 2
            && double.TryParse(numbers[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var visibility)
            && double.TryParse(numbers[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var maturity)
            ? [visibility, maturity]
            : throw new CelException($"'{values}' is not a position of an annotation.");
    }

    [GeneratedRegex(@"\[(?<values>[^\[\]]*)\]")]
    private static partial Regex Bracketed();
}
