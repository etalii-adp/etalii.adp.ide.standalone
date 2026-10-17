using EtAlii.Adp.Diagram.Modules;
using EtAlii.Adp.Specification.Cel;
using EtAlii.Adp.Specification.Disl;
using EtAlii.Adp.Specification.Fbl;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace EtAlii.Adp.Diagram.DependencyGraph;

/// <summary>
/// The findings of a dependency graph, as its definition states them (<c>constraints</c> in
/// <c>definition/dependency-graph.dis</c>), evaluated by the DISL runtime over the body's model
/// (<see cref="DependencyGraphBody.Disl"/>), and the definition's <c>writtenEnd</c> function they read.
/// </summary>
/// <remarks>
/// <para>
/// <b>The rules, codes and messages are the definition's</b>: a missing id, a duplicate one, a dangling
/// end and a self-dependency. Two things the model DISL hands CEL cannot hold are carried beside it, as
/// the binding's host attributes: <b>the id an element is written with</b> (<c>storedId</c>), which need
/// be neither present nor unique, so findings name an element by it (§11.5.4); and <b>the id a
/// dependency's end is written with</b> (<c>writtenFrom</c>, <c>writtenTo</c>), which is null in the model
/// when it names no node (§4.9) and which the definition's <c>writtenEnd</c> reads. That is what lets the
/// definition skip an end written empty and judge a self-dependency between two ids no node holds.
/// </para>
/// <para>
/// <b>The order is the one thing that stays code</b>: first the ids, a missing and a duplicate id
/// interleaved in declaration order, nodes before dependencies; then per dependency its dangling source,
/// its dangling target and its self-dependency. <c>constraints.order</c> sorts by code, so it cannot
/// interleave two, and it places a duplicate at the group's first member rather than at itself. Each
/// element's line in the model is its place in the tool's reading (<see cref="DependencyGraphReading"/>),
/// and the line it is reported at is looked up after.
/// </para>
/// <para>
/// <b>A repeated key is the one finding of the binding's own that is passed on</b> (Q2): FBL reads such a
/// body and cannot be told to refuse it, so it is a warning at its line, before the tool's own findings.
/// </para>
/// </remarks>
internal static class DependencyGraphFindings
{
    /// <summary>The host attribute holding the id an element or dependency is written with, empty for none.</summary>
    private const string StoredId = "storedId";

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

    /// <summary>The problems in <paramref name="body"/>, grouped as the tool reports them; the one finding of a body that is not YAML when it cannot be read.</summary>
    public static IReadOnlyList<DiagramProblem> Judge(DependencyGraphBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (body.IsUnreadable)
        {
            (string reason, int line) = UnreadableReason(body);
            return Unparseable(reason, line);
        }

        var reading = body.Reading;
        var findings = ConstraintEvaluator.Evaluate(
            DependencyGraphDefinition.Specification,
            body.Disl.Diagram,
            new DislConstraintOptions(WrittenId: element => DependencyGraphDefinition.HostText(element, StoredId)));
        return
        [
            .. body.Model.Findings
                .Where(finding => finding.Code == FindingCodes.DuplicateKey)
                .Select(finding => new DiagramProblem(DiagramProblemSeverity.Warning, finding.Message, finding.Code, new DiagramProblemLineLocation((uint)Math.Max(finding.Location?.Line ?? 1, 1)))),
            .. findings
                .Select((finding, arising) => (Finding: finding, Arising: arising))
                .OrderBy(entry => Groups.GetValueOrDefault(entry.Finding.Code, int.MaxValue))
                .ThenBy(entry => entry.Finding.Line ?? int.MaxValue)
                .ThenBy(entry => entry.Arising)
                .Select(entry => Problem(entry.Finding with { Line = entry.Finding.Line is { } at ? reading.LineOf(at) : null })),
        ];
    }

    /// <summary>The one finding of a document that is not YAML: the reader's <c>std.unparseable</c>, at the parser's line.</summary>
    private static IReadOnlyList<DiagramProblem> Unparseable(string reason, int line)
    {
        var findings = ConstraintEvaluator.Evaluate(
            DependencyGraphDefinition.Specification,
            new DislDiagram(DependencyGraphDefinition.Specification),
            new DislConstraintOptions(ReaderFindings: [DislReaderFinding.NotParsed(reason, line)]));
        return [.. findings.Select(Problem)];
    }

    /// <summary>
    /// Why a body cannot be read, and its 1-based line: YAML's own sentence and line, as YamlDotNet - the
    /// reader the library decides well-formedness with - states them, which is what the tool has always
    /// reported; else the library's, less its lead-in.
    /// </summary>
    internal static (string Reason, int Line) UnreadableReason(DependencyGraphBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        try
        {
            using var reader = new StringReader(body.Text);
            new YamlStream().Load(reader);
        }
        catch (YamlException exception)
        {
            return (exception.Message, (int)Math.Max(exception.Start.Line, 1));
        }

        const string leadIn = "The body is not well-formed YAML: ";
        var finding = body.Model.Findings.FirstOrDefault(candidate => candidate.Code == FindingCodes.Unparseable) ?? body.Model.Findings.FirstOrDefault();
        var message = finding?.Message ?? "it is not well-formed";
        return (message.StartsWith(leadIn, StringComparison.Ordinal) ? message[leadIn.Length..] : message, Math.Max(finding?.Location?.Line ?? 1, 1));
    }

    private static string WrittenEnd(object? relation, string? end) =>
        relation is DislElement element && end is "source" or "target"
            && element.HostAttributes.TryGetValue(end == "source" ? WrittenFrom : WrittenTo, out var written)
            ? written as string ?? ""
            : throw new CelException("writtenEnd() takes a dependency of this graph and 'source' or 'target'.");

    private static DiagramProblem Problem(DislFinding finding)
    {
        var severity = DislDiagramDefinition.SeverityOf(finding);

        // The ids and the file are reported at the line, because an element without an id, or with one
        // another declaration holds, cannot be named by it; everything else names its dependency.
        DiagramProblemLocation location = finding.Code is DependencyGraphRules.MissingId or DependencyGraphRules.DuplicateId or DependencyGraphValidator.UnparseableRuleId
            ? new DiagramProblemLineLocation((uint)(finding.Line ?? 1))
            : new DiagramProblemElementLocation(finding.ElementIds is [var id, ..] ? id : "");
        return new DiagramProblem(severity, finding.Message, finding.Code, location);
    }
}
