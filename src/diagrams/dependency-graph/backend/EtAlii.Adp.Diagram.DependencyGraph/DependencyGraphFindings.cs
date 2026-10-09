using EtAlii.Adp.Specification.Cel;
using EtAlii.Adp.Specification.Disl;
using EtAlii.Adp.Specification.Fbl;

namespace EtAlii.Adp.Diagram.DependencyGraph;

/// <summary>
/// The findings of a dependency graph, as its definition states them (<c>constraints</c> in
/// <c>definition/dependency-graph.dis</c>), evaluated by the DISL runtime over a model of what
/// <see cref="DependencyGraphParser"/> read, and the definition's <c>writtenEnd</c> function they read.
/// </summary>
/// <remarks>
/// <para>
/// <b>The rules, codes and messages are the definition's</b>: a missing id, a duplicate one, a dangling
/// end and a self-dependency. Two things the model DISL hands CEL cannot hold are carried beside it:
/// <b>the id an element is written with</b>, which need be neither present nor unique, so each element
/// gets a model id of its own and findings name it by the written one (§11.5.4); and <b>the id a
/// dependency's end is written with</b>, which is null in the model when it names no node (§4.9) and
/// which the definition's <c>writtenEnd</c> reads. That is what lets the definition skip an end written
/// empty and judge a self-dependency between two ids no node holds.
/// </para>
/// <para>
/// <b>The order is the one thing that stays code</b>: first the ids, a missing and a duplicate id
/// interleaved in declaration order, nodes before dependencies; then per dependency its dangling source,
/// its dangling target and its self-dependency. <c>constraints.order</c> sorts by code, so it cannot
/// interleave two, and it places a duplicate at the group's first member rather than at itself.
/// </para>
/// </remarks>
internal static class DependencyGraphFindings
{
    /// <summary>The host attribute holding the id an element or dependency is written with, empty for none.</summary>
    private const string WrittenId = "writtenId";

    /// <summary>The host attribute holding a dependency's <c>from</c> as written.</summary>
    private const string WrittenFrom = "writtenFrom";

    /// <summary>The host attribute holding a dependency's <c>to</c> as written.</summary>
    private const string WrittenTo = "writtenTo";

    /// <summary>The order the findings are reported in: the ids, then the dependencies' ends.</summary>
    private static readonly Dictionary<string, int> Groups = new(StringComparer.Ordinal)
    {
        [DependencyGraphRules.MissingId] = 0,
        [DependencyGraphRules.DuplicateId] = 0,
        [DependencyGraphRules.DanglingRelation] = 1,
        [DependencyGraphRules.SelfDependency] = 1,
    };

    /// <summary>The definition's plugin functions: <c>writtenEnd</c>.</summary>
    public static DislPluginFunctions Plugins() => new DislPluginFunctions()
        .Add("writtenEnd", arguments => WrittenEnd(arguments[0], arguments[1] as string));

    /// <summary>The problems in <paramref name="model"/>, grouped as the tool reports them.</summary>
    public static IReadOnlyList<DiagramProblem> Judge(DependencyGraphModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        (DislDiagram diagram, List<int> lines) = Build(model);
        var findings = ConstraintEvaluator.Evaluate(
            DependencyGraphDefinition.Specification,
            diagram,
            new DislConstraintOptions(WrittenId: WrittenIdOf));
        return
        [
            .. findings
                .Select((finding, arising) => (Finding: finding, Arising: arising))
                .OrderBy(entry => Groups.GetValueOrDefault(entry.Finding.Code, int.MaxValue))
                .ThenBy(entry => entry.Finding.Line ?? int.MaxValue)
                .ThenBy(entry => entry.Arising)
                .Select(entry => Problem(entry.Finding with { Line = entry.Finding.Line is { } at ? lines[at - 1] : null })),
        ];
    }

    /// <summary>The one finding of a document that is not YAML: the reader's <c>std.unparseable</c>, at the parser's line.</summary>
    public static IReadOnlyList<DiagramProblem> Unparseable(string reason, int line)
    {
        var findings = ConstraintEvaluator.Evaluate(
            DependencyGraphDefinition.Specification,
            new DislDiagram(DependencyGraphDefinition.Specification),
            new DislConstraintOptions(ReaderFindings: [DislReaderFinding.NotParsed(reason, line)]));
        return [.. findings.Select(Problem)];
    }

    /// <summary>
    /// The model the rules judge: a node per element and a dependency per relation, each with the id it is
    /// written with beside it; and each declaration's first line in the file.
    /// </summary>
    /// <remarks>
    /// The runtime reads "first" and "later" by line, and this format reads every node before any
    /// dependency, also when the relations are written above the elements. So each declaration's line in the
    /// model is its place in that reading, nodes first, and the line it is reported at is looked up after.
    /// </remarks>
    private static (DislDiagram Diagram, List<int> Lines) Build(DependencyGraphModel model)
    {
        var written = model.Elements.Select(element => element.Id).Concat(model.Relations.Select(relation => relation.Id)).ToHashSet(StringComparer.Ordinal);
        var taken = new HashSet<string>(StringComparer.Ordinal);
        var ephemeral = 0;

        var nodeIds = new Dictionary<string, string>(StringComparer.Ordinal);
        var lines = new List<int>();
        var read = new List<FblElement>();
        foreach (var element in model.Elements)
        {
            var id = ModelId(element.Id);
            nodeIds.TryAdd(element.Id, id);
            var attributes = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["label"] = element.Label,
                ["x"] = element.X,
                ["row"] = (long)element.Row,
                [WrittenId] = element.Id,
            };
            lines.Add(element.Range.Start + 1);
            read.Add(new FblElement(id, true, "Node", "element", false, attributes, null, null, null, null, default, lines.Count));
        }

        foreach (var relation in model.Relations)
        {
            var attributes = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["label"] = relation.Label,
                [WrittenId] = relation.Id,
                [WrittenFrom] = relation.From,
                [WrittenTo] = relation.To,
            };
            read.Add(new FblElement(
                ModelId(relation.Id), true, "DependsOn", "relation", true, attributes, null, null,
                nodeIds.GetValueOrDefault(relation.From), nodeIds.GetValueOrDefault(relation.To), default, lines.Count + 1));
            lines.Add(relation.Range.Start + 1);
        }

        return (DislModelBuilder.From(new FblModel(read, [], false), DependencyGraphDefinition.Specification).Diagram, lines);

        string ModelId(string id)
        {
            if (id.Length > 0 && taken.Add(id)) return id;
            string unnamed;
            do unnamed = $"dependencies:unnamed:{++ephemeral}";
            while (written.Contains(unnamed) || !taken.Add(unnamed));
            return unnamed;
        }
    }

    private static string WrittenIdOf(DislElement element) =>
        element.HostAttributes.TryGetValue(WrittenId, out var id) ? id as string ?? "" : "";

    private static string WrittenEnd(object? relation, string? end) =>
        relation is DislElement element && end is "source" or "target"
            && element.HostAttributes.TryGetValue(end == "source" ? WrittenFrom : WrittenTo, out var written)
            ? written as string ?? ""
            : throw new CelException("writtenEnd() takes a dependency of this graph and 'source' or 'target'.");

    private static DiagramProblem Problem(DislFinding finding)
    {
        var severity = finding.Severity == "error" ? DiagramProblemSeverity.Error : DiagramProblemSeverity.Warning;

        // The ids and the file are reported at the line, because an element without an id, or with one
        // another declaration holds, cannot be named by it; everything else names its dependency.
        DiagramProblemLocation location = finding.Code is DependencyGraphRules.MissingId or DependencyGraphRules.DuplicateId or DependencyGraphValidator.UnparseableRuleId
            ? new DiagramProblemLineLocation((uint)(finding.Line ?? 1))
            : new DiagramProblemElementLocation(finding.ElementIds is [var id, ..] ? id : "");
        return new DiagramProblem(severity, finding.Message, finding.Code, location);
    }
}
