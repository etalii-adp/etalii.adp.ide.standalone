using System.Text;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Specification.Cel;
using EtAlii.Adp.Specification.Disl;
using EtAlii.Adp.Specification.Fbl;
using EtAlii.Adp.Specification.Fbl.Plugins;
using Path = System.IO.Path;

namespace EtAlii.Adp.Diagram.Rdf;

/// <summary>
/// The FBL §11 persistence plugin of the binding <c>w3c-turtle.fbl#turtle</c> (<c>net.etalii.adp.w3c.turtle</c>):
/// its <c>read</c> is the module's <see cref="RdfTokenizer"/> and <see cref="RdfParser"/>, delivering the stored
/// elements the four definitions name - one <c>Triple</c> per triple, one <c>Prefix</c> per declaration and one
/// <c>Base</c> - in document order, with the five <c>rdf.*</c> findings at the code, severity, sentence and line
/// <see cref="RdfValidator"/> gives them.
/// </summary>
/// <remarks>
/// <para>
/// <b>Spans are UTF-8 byte ranges of the bytes it is handed</b>, a byte-order mark counted (finding B5): the
/// parser's offsets are UTF-16 offsets into the text after the mark. An element's <see cref="FblElement.OwnSpan"/>
/// is its statement's range, from the statement's first token through its <c>.</c>, or the directive's for a
/// <c>Prefix</c> and a <c>Base</c>; a triple inside an inline blank property list carries the enclosing statement's.
/// </para>
/// <para>
/// <b>A value has a span only where one element alone writes it</b> (Requirement 3.4): an object written once, the
/// language tag and the datatype written with it, a predicate no <c>,</c> list shares and a subject no <c>;</c> list
/// shares. A token several triples read - a subject written once for a <c>;</c> list, a predicate written once for a
/// <c>,</c> list - is delivered with no value span, so no two elements claim one span. That the contract cannot say
/// that such a value is shared is gap 3, shown by <c>shared-statement.ttl</c>.
/// </para>
/// <para>
/// <b>It plans nothing in this stage</b> (Q5): <see cref="Plan"/> refuses every change with one sentence, and no
/// command calls it, because every edit is still written by <see cref="RdfWriter"/> and the SHACL writer until FBL
/// answers gaps 1 and 2. <see cref="Template"/> gives the <c>w3c/rdf</c> starter and is never asked, because the
/// binding states its templates.
/// </para>
/// <para>
/// <b>It keeps no state and reads nothing but what it is handed</b> (FBL §11.4, Requirement 3.5): no registration,
/// no file system, no clock. The syntax it is constructed with is the stand-in for gap 5 (<see cref="RdfSyntax"/>).
/// Reading never throws on content: a body that does not parse is delivered unreadable, with no element and the one
/// finding <c>rdf.unparseable</c> on the parser's line.
/// </para>
/// </remarks>
/// <param name="syntax">The body's syntax, from its file's extension (<see cref="RdfBody"/>); neither reading nor this stage's plan differs by it.</param>
public sealed class RdfTurtlePlugin(RdfSyntax syntax = RdfSyntax.Turtle) : IPersistencePlugin
{
    /// <summary>The plugin's id, as the binding's <c>reader.plugin</c> and the four definitions' <c>plugins</c> name it.</summary>
    private const string PluginId = "net.etalii.adp.w3c.turtle";

    /// <summary>The one sentence every change is refused with in this stage.</summary>
    public const string PlanRefusal = "An RDF file is written by its commands, not planned by the Turtle plugin, until FBL can plan an edit of several triples as one.";

    private static readonly byte[] _byteOrderMark = [0xEF, 0xBB, 0xBF];

    /// <inheritdoc />
    public string Id => PluginId;

    /// <summary>The syntax this plugin was told the body is written in.</summary>
    public RdfSyntax Syntax { get; } = syntax;

    /// <inheritdoc />
    public PluginReadResult Read(PluginReadRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var bytes = Body(request.Files);
        var mark = bytes.AsSpan().StartsWith(_byteOrderMark) ? _byteOrderMark.Length : 0;
        var text = Encoding.UTF8.GetString(bytes, mark, bytes.Length - mark);
        var outcome = RdfParser.Outcome(LineDocument.Parse(text));
        if (!outcome.IsUsable)
        {
            var problem = outcome.Problems[0];
            return new PluginReadResult([], [Unparseable(problem)], true);
        }

        var reading = new RdfTurtleReading(text, mark, outcome.Model);
        return new PluginReadResult(reading.Elements(), Findings(outcome.Model), false);
    }

    /// <inheritdoc />
    public PluginPlanResult Plan(PluginPlanRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new PluginPlanResult.Refused(PlanRefusal);
    }

    /// <inheritdoc />
    public byte[] Template(PluginTemplateRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Encoding.UTF8.GetBytes(RdfDefinitions.NewDocument(ServiceCollectionAddRdfExtension.RdfOrigin, Path.GetFileNameWithoutExtension(request.Name)));
    }

    /// <inheritdoc />
    public IReadOnlyList<string> Watch(PluginReadResult last) => [];

    /// <summary>
    /// The plugin's CEL functions (DISL §13.1.1) this module implements, for the four definitions: <c>rdfLocalName</c>,
    /// <c>rdfDisplayName</c> and <c>rdfResolveTerm</c>, each reading the <c>Prefix</c> elements of the diagram it is
    /// called on and keeping nothing. The others the definitions declare fall back as their declarations say until a
    /// reading derives with them.
    /// </summary>
    public static DislPluginFunctions Functions() => new DislPluginFunctions()
        .Add("rdfLocalName", arguments => LocalName(Text(arguments, "rdfLocalName")))
        .Add("rdfDisplayName", (arguments, used) => DisplayName(Diagram(used, "rdfDisplayName"), Text(arguments, "rdfDisplayName")))
        .Add("rdfResolveTerm", (arguments, used) => ResolveTerm(Diagram(used, "rdfResolveTerm"), Text(arguments, "rdfResolveTerm")));

    /// <summary>The part of <paramref name="iri"/> after its last <c>#</c> or <c>/</c>, or the whole IRI without either.</summary>
    internal static string LocalName(string iri) => iri[(Math.Max(iri.LastIndexOf('#'), iri.LastIndexOf('/')) + 1)..];

    /// <summary><paramref name="iri"/> as drawn: a prefixed name under the diagram's prefixes, else its local name, else the IRI.</summary>
    internal static string DisplayName(DislDiagram diagram, string iri) => RdfProjection.Display(ModelOf(diagram), new IriTerm(iri, ""));

    /// <summary>The full IRI the typed <paramref name="term"/> names under the diagram's prefixes; empty when it names nothing.</summary>
    internal static string ResolveTerm(DislDiagram diagram, string term) => RdfTermInput.Resolve(ModelOf(diagram), term).Iri ?? "";

    /// <summary>The prefixes and the base the diagram's stored <c>Prefix</c> and <c>Base</c> elements state, as the model the module's own functions read.</summary>
    private static RdfModel ModelOf(DislDiagram diagram)
    {
        List<PrefixDeclaration> prefixes = [];
        foreach (var prefix in diagram.NodesOfType("Prefix"))
        {
            var line = prefix.Attributes.GetValueOrDefault("line") is long number ? (int)number - 1 : -1;
            prefixes.Add(new PrefixDeclaration(prefix.Attributes.GetValueOrDefault("name") as string ?? "", prefix.Attributes.GetValueOrDefault("iri") as string ?? "", line));
        }

        var stated = diagram.NodesOfType("Base").FirstOrDefault()?.Attributes.GetValueOrDefault("iri") as string;
        return new RdfModel([], prefixes, stated, -1);
    }

    private static string Text(IReadOnlyList<object?> arguments, string function) =>
        arguments is [string text] ? text : throw new CelException($"{function}() takes one string.");

    private static DislDiagram Diagram(IReadOnlyDictionary<string, object?> used, string function) =>
        used.GetValueOrDefault("diagram") as DislDiagram ?? throw new CelException($"{function}() reads the diagram it is called on.");

    /// <summary>The five <c>rdf.*</c> findings of a body that parses, as <see cref="RdfValidator"/> gives them.</summary>
    private static IReadOnlyList<Finding> Findings(RdfModel model) =>
        [.. RdfValidator.Judge(model).Select(problem => new Finding(problem.RuleId, SeverityOf(problem.Severity), problem.Message, LocationOf(problem.Location)))];

    /// <summary>The one finding of a body that does not parse, as <see cref="RdfValidator"/> gives it.</summary>
    private static Finding Unparseable(ParseProblem problem) =>
        new(RdfValidator.UnparseableRuleId, FindingSeverity.Error, $"This is not RDF that can be read: {problem.Message}", new SourceLocation("", problem.Line + 1, 1, 0));

    private static FindingSeverity SeverityOf(DiagramProblemSeverity severity) => severity switch
    {
        DiagramProblemSeverity.Error => FindingSeverity.Error,
        DiagramProblemSeverity.Warning => FindingSeverity.Warning,
        _ => FindingSeverity.Info,
    };

    private static SourceLocation? LocationOf(DiagramProblemLocation? location) =>
        location is DiagramProblemLineLocation line ? new SourceLocation("", (int)line.Number, 1, 0) : null;

    private static byte[] Body(IReadOnlyList<PluginFile> files) =>
        files.FirstOrDefault(file => file.RelativePath.Length == 0)?.Bytes ?? throw new ArgumentException("An RDF document is a file body.", nameof(files));
}
