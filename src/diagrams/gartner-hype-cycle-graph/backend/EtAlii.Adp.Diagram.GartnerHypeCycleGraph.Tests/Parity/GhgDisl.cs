using EtAlii.Adp.Specification.Cel;
using EtAlii.Adp.Specification.Disl;

namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph.Tests.Parity;

/// <summary>
/// The bundled hype cycle graph definition, and the corpus as its CEL sees it: each parsed model put
/// into a <see cref="DislDiagram"/> by hand, field for field, so the definition's functions can be
/// compared with the code on exactly the values the code computed with.
/// </summary>
internal static class GhgDisl
{
    private static readonly Dictionary<string, CelProgram> Programs = new(StringComparer.Ordinal);

    public static DislSpecification Specification { get; } =
        BundledDefinition.Load(typeof(GhgParser).Assembly, "gartner-hype-cycle-graph.dis").Specification;

    /// <summary>Every document of the parity corpus, parsed: <c>src/</c>-relative path and model.</summary>
    public static IEnumerable<(string Path, GhgModel Model)> Corpus()
    {
        var source = Path.GetFullPath(Path.Combine(GhgTranscript.ModuleFolder, "..", ".."));
        return GhgTranscript.Corpus().Select(relative => (relative, GhgParser.Parse(File.ReadAllText(Path.Combine(source, relative)))));
    }

    /// <summary>A diagram of <paramref name="model"/>'s unit holding its trends, in document order.</summary>
    public static DislDiagram DiagramOf(GhgModel model)
    {
        var diagram = new DislDiagram(Specification, model.Unit is { } unit ? new Dictionary<string, object?> { ["unit"] = unit.Name } : null);
        foreach (var trend in model.Trends) TrendOf(diagram, trend);
        return diagram;
    }

    /// <summary>Adds <paramref name="trend"/> as a Trend: the values it holds stored, those it lacks left unset.</summary>
    public static DislElement TrendOf(DislDiagram diagram, GhgTrend trend)
    {
        var attributes = new Dictionary<string, object?>
        {
            ["name"] = trend.Name,
            ["phases"] = (long)trend.Phases,
            ["row"] = (long)trend.Row,
            ["tags"] = trend.Tags.Cast<object?>().ToList(),
        };
        if (trend.Start is { } start) attributes["start"] = (long)start;
        if (trend.Stop is { } stop) attributes["stop"] = (long)stop;
        string[] keys = ["peakEnd", "troughEnd", "slopeEnd"];
        for (var index = 0; index < Math.Min(keys.Length, trend.DraggedEnds.Count); index++)
        {
            if (trend.DraggedEnds[index] is { } boundary) attributes[keys[index]] = (long)boundary;
        }
        return diagram.AddNode("Trend", trend.Id, attributes);
    }

    /// <summary>Evaluates <paramref name="expression"/> in the element context, <paramref name="variables"/> bound beside <c>self</c>, <c>diagram</c> and <c>env</c>.</summary>
    public static object? Evaluate(string expression, IReadOnlyDictionary<string, object?> variables)
    {
        CelProgram? program;
        lock (Programs)
        {
            if (!Programs.TryGetValue(expression, out program))
            {
                program = Programs[expression] = Specification.Environment(DislContexts.Element, variables.Keys).Compile(expression);
            }
        }
        var value = program.Evaluate(variables);
        return value is CelError error ? throw new InvalidOperationException($"{expression}: {error.Message}") : value;
    }

    /// <summary>The compiled expression at <paramref name="pointer"/> evaluated with <paramref name="variables"/>.</summary>
    public static object? EvaluateAt(string pointer, IReadOnlyDictionary<string, object?> variables)
    {
        var expression = Specification.ExpressionAt(pointer) ?? throw new InvalidOperationException($"No expression at {pointer}.");
        var value = expression.Program.Evaluate(variables);
        return value is CelError error ? throw new InvalidOperationException($"{pointer}: {error.Message}") : value;
    }

    /// <summary>The index of the constraint rule with <paramref name="id"/>.</summary>
    public static int RuleIndex(string id) =>
        Specification.Root.GetProperty("constraints").GetProperty("rules").EnumerateArray()
            .Select((rule, index) => (rule, index))
            .Single(pair => pair.rule.GetProperty("id").GetString() == id).index;
}
