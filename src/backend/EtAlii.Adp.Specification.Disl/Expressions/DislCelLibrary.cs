using System.Globalization;
using System.Text.RegularExpressions;
using EtAlii.Adp.Specification.Cel;

namespace EtAlii.Adp.Specification.Disl;

/// <summary>
/// The DISL function library (§12.4) and the element methods of §12.2 that this runtime offers, on
/// top of CEL's standard environment. A specification that calls anything else is refused at load,
/// naming it.
/// </summary>
/// <remarks>
/// The element methods are declared only: <c>isA</c>, <c>descendants</c> and the rest are
/// implemented by the model's elements (<see cref="ICelObject"/>), and calling one on any other value
/// is an evaluation error.
/// </remarks>
internal static class DislCelLibrary
{
    /// <summary>The methods of DISL's Element and Diagram types (§12.2) this runtime implements, with their arities.</summary>
    private static readonly (string Name, int Min, int Max)[] Methods =
    [
        ("isA", 1, 1),
        ("descendants", 0, 0),
        ("ancestors", 0, 0),
        ("childrenOfType", 1, 1),
        ("incomingOf", 1, 1),
        ("outgoingOf", 1, 1),
        ("reachable", 1, 1),
        ("inCycle", 1, 1),
        ("positionIn", 1, 1),
        ("nodesOfType", 1, 2),
        ("relationsOfType", 1, 1),
        ("elementById", 1, 1),
        ("label", 0, 0),
        ("other", 1, 1),
        ("location", 0, 0),
    ];

    /// <summary>
    /// Adds the library to <paramref name="environment"/>; <paramref name="enums"/> finds the enumerations <c>enumLabel</c>
    /// reads, and <paramref name="axes"/> the axis ranges <c>axisRange</c> reads.
    /// </summary>
    public static void Register(CelEnvironment environment, Func<string, DislEnum?> enums, DislAxisRanges axes)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(enums);
        ArgumentNullException.ThrowIfNull(axes);

        foreach ((string name, int min, int max) in Methods)
        {
            environment.AddFunction(CelFunction.Method(name, min, max));
        }

        environment.AddFunction(CelFunction.Receiver("cycles", 2, arguments => Cycles(arguments).Cycles.Select(cycle => (object?)cycle.Cast<object?>().ToList()).ToList(), CyclesCost));
        environment.AddFunction(CelFunction.Receiver("cyclesTruncated", 2, arguments => Cycles(arguments).Truncated, CyclesCost));
        environment.AddFunction(CelFunction.Receiver("knots", 1, Knots, KnotsCost));
        environment.AddFunction(new CelFunction("budget", CelCallStyle.Global, 0, 1, Budget, _ => 1));
        environment.AddFunction(CelFunction.Global("enumLabel", 2, arguments => EnumLabel(enums, arguments[0], arguments[1])));
        environment.AddFunction(CelFunction.Global("axisRange", 2, arguments => AxisRange(axes, arguments[0], arguments[1])));
        environment.AddFunction(CelFunction.Receiver("avg", 0, arguments => Average(arguments[0]), arguments => arguments[0] is IReadOnlyList<object?> list ? list.Count : 1));
        environment.AddFunction(CelFunction.Receiver("sum", 0, arguments => Sum(arguments[0]), arguments => arguments[0] is IReadOnlyList<object?> list ? list.Count : 1));
        environment.AddFunction(CelFunction.Global("formatNumber", 2, arguments => FormatNumber(arguments[0], arguments[1])));
        environment.AddFunction(CelFunction.Receiver("center", 0, arguments => Center(arguments[0])));
        environment.AddFunction(CelFunction.Global("token", 1, arguments => throw new CelException($"The theme token '{Text(arguments[0])}' is resolved where the diagram is drawn; this runtime has no theme.")));
        environment.AddFunction(CelFunction.Global("color", 1, arguments => DislColor.Of(arguments[0])));
        environment.AddFunction(CelFunction.Receiver("mix", 2, arguments => DislColor.Mix(arguments[0], arguments[1], arguments[2])));
        environment.AddFunction(CelFunction.Global("clamp", 3, arguments => Clamp(arguments[0], arguments[1], arguments[2])));
        environment.AddFunction(new CelFunction("min", CelCallStyle.Global, 2, 4, call => Extreme(call.Arguments, -1)));
        environment.AddFunction(new CelFunction("max", CelCallStyle.Global, 2, 4, call => Extreme(call.Arguments, 1)));
        environment.AddFunction(CelFunction.Global("yearMonth", 2, arguments => YearMonth.Of(Int(arguments[0]), Int(arguments[1]))));
        environment.AddFunction(CelFunction.Global("formatYearMonth", 2, arguments => YearMonth.Format(Int(arguments[0]), Text(arguments[1]))));
        environment.AddFunction(CelFunction.Global("parseYearMonth", 1, arguments =>
            YearMonth.Parse(Text(arguments[0])) is { } index ? CelOptional.Of(index) : CelOptional.None));
        environment.AddFunction(CelFunction.Receiver("year", 0, arguments => YearMonth.YearOf(Int(arguments[0]))));
        environment.AddFunction(CelFunction.Receiver("month", 0, arguments => YearMonth.MonthOf(Int(arguments[0]))));
    }

    /// <summary><c>axisRange(axis, value)</c> (§12.4): the id of the range of the axis that holds the value, or <c>''</c>.</summary>
    private static string AxisRange(DislAxisRanges axes, object? axis, object? value)
    {
        var name = Text(axis);
        return axes.Holding(name, Number(value)) ?? throw new CelException($"'{name}' is not an axis of this specification (§5.3).");
    }

    /// <summary><c>diagram.knots(relType)</c> (§12.4), on the diagram that receives it.</summary>
    private static object Knots(IReadOnlyList<object?> arguments)
    {
        var diagram = Receiving(arguments[0]) ?? throw new CelException("knots() is a method of the diagram.");
        return diagram.Knots(Text(arguments[1])).Select(knot => (object?)knot.Cast<object?>().ToList()).ToList();
    }

    /// <summary>A knot search is proportional to the relations of the type (§12.4).</summary>
    private static long KnotsCost(IReadOnlyList<object?> arguments) =>
        Receiving(arguments[0]) is { } diagram ? diagram.Relations.Count + diagram.Nodes.Count + 1 : 1;

    /// <summary>
    /// <c>budget(id)</c> (§12.4): the state of a budget of the diagram the calling expression has, as a map of
    /// <c>shown</c>, <c>total</c>, <c>truncated</c> and <c>withheld</c>.
    /// </summary>
    private static object Budget(CelCall call)
    {
        var diagram = (call.Variables.TryGetValue("diagram", out var value) ? Receiving(value) : null)
            ?? throw new CelException("budget() reads the budgets of the diagram, which this expression has none of.");
        if (call.Arguments.Count == 0)
        {
            // Every budget at once: truncated and withheld when they are for any.
            var states = diagram.Budgets;
            return new CelMap
            {
                ["shown"] = states.Sum(state => state.Shown),
                ["total"] = states.Sum(state => state.Total),
                ["truncated"] = states.Any(state => state.Truncated),
                ["withheld"] = states.Any(state => state.Withheld),
            };
        }
        var id = Text(call.Arguments[0]);
        var budget = diagram.Budget(id) ?? throw new CelException($"'{id}' is not a budget of this specification (§3.2.1).");
        return new CelMap
        {
            ["shown"] = budget.Shown,
            ["total"] = budget.Total,
            ["truncated"] = budget.Truncated,
            ["withheld"] = budget.Withheld,
        };
    }

    /// <summary>The diagram a value is, also where a drawn diagram stands in for it.</summary>
    private static DislDiagram? Receiving(object? value) => value switch
    {
        DislDiagram diagram => diagram,
        DislDrawnDiagram drawn => drawn.Diagram,
        _ => null,
    };

    /// <summary><c>diagram.cycles(relType, max)</c> and <c>diagram.cyclesTruncated(relType, max)</c> (§12.4), on the diagram that receives them.</summary>
    private static (IReadOnlyList<IReadOnlyList<DislElement>> Cycles, bool Truncated) Cycles(IReadOnlyList<object?> arguments)
    {
        var diagram = arguments[0] as DislDiagram ?? throw new CelException("cycles() and cyclesTruncated() are methods of the diagram.");
        var max = Int(arguments[2]);
        return max < 0 ? throw new CelException($"The bound of cycles() is a count, not {max}.") : diagram.Cycles(Text(arguments[1]), max);
    }

    /// <summary>The cost §12.4 sets for the cycle search: proportional to (nodes + relations) × (<c>max</c> + 1), so a bound keeps CEL's termination guarantee.</summary>
    private static long CyclesCost(IReadOnlyList<object?> arguments) =>
        arguments is [DislDiagram diagram, _, long and >= 0 and var max, ..]
            ? (long)(diagram.Nodes.Count + diagram.Relations.Count) is var size && max >= long.MaxValue / Math.Max(size, 1) ? long.MaxValue : size * (max + 1)
            : 1;

    /// <summary>The label of an enumeration's value (§4.5): its declared label, else its key; a value the enumeration lacks, as an extensible one may hold, is its own label.</summary>
    private static string EnumLabel(Func<string, DislEnum?> enums, object? name, object? value)
    {
        var enumeration = enums(Text(name)) ?? throw new CelException($"'{name}' is not an enumeration of this specification.");
        var key = Text(value);
        return enumeration.ValueOf(key) is { } known ? known.Label ?? known.Key : key;
    }

    /// <summary><c>l.avg()</c> (§12.4): the mean of a list of ints and doubles, as a double; an empty list has none.</summary>
    private static double Average(object? value)
    {
        var list = value as IReadOnlyList<object?> ?? throw new CelException("avg() takes a list of numbers.");
        if (list.Count == 0) throw new CelException("avg() of an empty list has no value.");
        return list.Sum(item => item is long or double ? Number(item) : throw new CelException("avg() takes a list of numbers.")) / list.Count;
    }

    /// <summary>
    /// <c>l.sum()</c> (§12.4): the total of a list of numbers, an int while every item is an int and a
    /// double once one is not; the total of an empty list is the int 0.
    /// </summary>
    private static object Sum(object? value)
    {
        var list = value as IReadOnlyList<object?> ?? throw new CelException("sum() takes a list of numbers.");
        if (list.All(item => item is long))
        {
            long total = 0;
            foreach (var item in list)
            {
                try
                {
                    total = checked(total + (long)item!);
                }
                catch (OverflowException)
                {
                    throw new CelException("sum() of these ints overflows.");
                }
            }
            return total;
        }
        return list.Sum(item => item is long or double ? Number(item) : throw new CelException("sum() takes a list of numbers."));
    }

    /// <summary>The number patterns <c>formatNumber</c> writes: digit placeholders with grouping, and decimals after one point.</summary>
    private static readonly Regex NumberPattern = new(@"^[#,]*0[0,]*(\.[0#]+)?$|^#[#,]*(\.[0#]+)?$", RegexOptions.CultureInvariant);

    /// <summary>
    /// <c>formatNumber(n, pattern)</c> (§12.4): <paramref name="number"/> written by <paramref name="pattern"/>, with
    /// <c>0</c> a digit always written, <c>#</c> one written when it is significant, <c>,</c> grouping by
    /// thousands and <c>.</c> the decimal point, in the invariant culture. A half in the last kept decimal
    /// rounds as .NET's custom numeric formats round it, which is what the hosts of this runtime write.
    /// </summary>
    private static string FormatNumber(object? number, object? pattern)
    {
        if (number is not (long or double) || pattern is not string text) throw new CelException("formatNumber() takes a number and a pattern.");
        if (!NumberPattern.IsMatch(text))
        {
            throw new CelException($"'{text}' is not a number pattern this runtime writes: digits '0' and '#', grouping ',' and one decimal point '.'.");
        }
        return number is long integer ? integer.ToString(text, CultureInfo.InvariantCulture) : ((double)number).ToString(text, CultureInfo.InvariantCulture);
    }

    /// <summary><c>b.center()</c> on a Bounds (§12.2): <c>[x + width / 2, y + height / 2]</c>, as doubles.</summary>
    private static List<object?> Center(object? value)
    {
        if (value is not CelMap bounds || !bounds.TryGetValue("x", out var x) || !bounds.TryGetValue("y", out var y)
            || !bounds.TryGetValue("width", out var width) || !bounds.TryGetValue("height", out var height)
            || x is not (long or double) || y is not (long or double) || width is not (long or double) || height is not (long or double))
        {
            throw new CelException("center() needs bounds with x, y, width and height.");
        }
        return [Number(x) + Number(width) / 2, Number(y) + Number(height) / 2];
    }

    /// <summary><c>clamp(v, lo, hi)</c>: <c>lo</c> below it, <c>hi</c> above it, otherwise <c>v</c> itself, of whichever kind each is.</summary>
    private static object? Clamp(object? value, object? low, object? high) =>
        Number(value) < Number(low) ? low : Number(value) > Number(high) ? high : value;

    /// <summary>The smallest (<paramref name="sign"/> -1) or largest (1) argument, itself rather than converted, the first of equals; as <c>math.least</c> and <c>math.greatest</c>.</summary>
    private static object? Extreme(IReadOnlyList<object?> arguments, int sign)
    {
        var best = arguments[0];
        foreach (var candidate in arguments.Skip(1))
        {
            if (Number(candidate).CompareTo(Number(best)) * sign > 0) best = candidate;
        }
        return best;
    }

    private static double Number(object? value) => value switch
    {
        long integer => integer,
        double real => real,
        _ => throw new CelException("A number was expected."),
    };

    private static long Int(object? value) => value as long? ?? throw new CelException("An int was expected.");

    private static string Text(object? value) => value as string ?? throw new CelException("A string was expected.");
}
