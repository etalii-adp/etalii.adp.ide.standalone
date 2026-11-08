using System.Text;
using EtAlii.Adp.Diagram.Rdf.Tests.Parity;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Files;
using EtAlii.Adp.History;
using EtAlii.Adp.Specification.Disl;
using EtAlii.Adp.Specification.Fbl;
using EtAlii.Adp.Specification.Fbl.Planning;
using EtAlii.Adp.Specification.Fbl.Plugins;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.Rdf.Tests;

/// <summary>
/// The persistence plugin <c>net.etalii.adp.w3c.turtle</c> (rdf-disl-fbl task 7): its spans are UTF-8 byte ranges
/// that count a byte-order mark, a value shared by several triples has no span, a body that does not parse is
/// unreadable with today's finding, its plan refuses, and its CEL functions read the diagram's prefixes.
/// </summary>
public class RdfTurtlePluginTests
{
    private static byte[] Fixture(string name) => File.ReadAllBytes(IoPath.Combine(AppContext.BaseDirectory, "Fixtures", name));

    private static PluginReadResult Read(byte[] bytes) => new RdfTurtlePlugin().Read(new PluginReadRequest([new PluginFile("", bytes)]));

    private static string Slice(byte[] bytes, Span span) => Encoding.UTF8.GetString(bytes, span.Start, span.End - span.Start);

    private static FblElement Triple(PluginReadResult reading, string subject, string predicate, string? value = null) =>
        reading.Elements.Single(element => element.Type == "Triple" && (string?)element.Attributes["s"] == subject && (string?)element.Attributes["p"] == predicate
            && (value is null || (string?)element.Attributes["o"] == value));

    [Fact]
    public void TheSpans_AreUtf8ByteRanges_AfterACyrillicLiteral()
    {
        // Arrange: the shipped example whose last literal is Cyrillic, so a UTF-16 offset is not its byte offset.
        var bytes = File.ReadAllBytes(IoPath.Combine(RdfCorpus.SourceFolder, "diagrams", "rdf", "examples", "w3c-turtle", "example-1.ttl"));

        // Act.
        var reading = Read(bytes);

        // Assert.
        var name = Triple(reading, "http://example.org/#spiderman", "http://xmlns.com/foaf/0.1/name", "Человек-паук");
        Assert.Equal("\"Человек-паук\"", Slice(bytes, name.ValueSpans["o"]));
        Assert.Equal("ru", Slice(bytes, name.ValueSpans["language"]));
        Assert.EndsWith("foaf:name \"Spiderman\", \"Человек-паук\"@ru .", Slice(bytes, name.OwnSpan), StringComparison.Ordinal);
        Assert.StartsWith("<#spiderman>", Slice(bytes, name.OwnSpan), StringComparison.Ordinal);
    }

    [Fact]
    public void TheSpans_CountTheByteOrderMark()
    {
        // Arrange: the fixture of finding B5, which starts with a byte-order mark.
        var bytes = Fixture("byte-order-mark.ttl");
        Assert.Equal([0xEF, 0xBB, 0xBF], bytes[..3]);

        // Act.
        var reading = Read(bytes);

        // Assert.
        var prefix = reading.Elements.First(element => element.Type == "Prefix");
        Assert.Equal("@prefix ex: <http://example.org/> .", Slice(bytes, prefix.OwnSpan));
        var moscow = Triple(reading, "http://example.org/moscow", "http://www.w3.org/2000/01/rdf-schema#label");
        Assert.Equal("\"Москва\"", Slice(bytes, moscow.ValueSpans["o"]));
        var prague = Triple(reading, "http://example.org/prague", "http://www.w3.org/2000/01/rdf-schema#label");
        Assert.Equal("ex:prague rdfs:label \"Praha\"@cs .", Slice(bytes, prague.OwnSpan));
        Assert.Equal(("ex:prague", "rdfs:label", "\"Praha\"", "cs"), (Slice(bytes, prague.ValueSpans["s"]), Slice(bytes, prague.ValueSpans["p"]), Slice(bytes, prague.ValueSpans["o"]), Slice(bytes, prague.ValueSpans["language"])));
    }

    [Fact]
    public void AValueWrittenOnceForSeveralTriples_HasNoSpan_AndOneWrittenForOneHasItsOwn()
    {
        // Arrange: finding B1's fixture, a subject shared by a ';' list and a predicate shared by a ',' list.
        var bytes = Fixture("shared-statement.ttl");

        // Act.
        var reading = Read(bytes);

        // Assert.
        const string ex = "http://example.org/";
        var knowsBob = Triple(reading, ex + "alice", ex + "knows", ex + "bob");
        var knowsCarol = Triple(reading, ex + "alice", ex + "knows", ex + "carol");
        var worksWith = Triple(reading, ex + "alice", ex + "worksWith");
        Assert.Equal(["o"], knowsBob.ValueSpans.Keys);
        Assert.Equal(["o"], knowsCarol.ValueSpans.Keys);
        Assert.Equal(["o", "p"], worksWith.ValueSpans.Keys.Order(StringComparer.Ordinal));
        Assert.Equal("ex:worksWith", Slice(bytes, worksWith.ValueSpans["p"]));
        Assert.Equal(knowsBob.OwnSpan, worksWith.OwnSpan);
        var carol = Triple(reading, ex + "carol", ex + "knows");
        Assert.Equal(["o", "p", "s"], carol.ValueSpans.Keys.Order(StringComparer.Ordinal));
        Assert.Equal("ex:carol ex:knows ex:bob .", Slice(bytes, carol.OwnSpan));
    }

    [Fact]
    public void AToken_TwoTriplesBothSpell_IsTheSpanOfNeither()
    {
        // Arrange: a collection whose one element is rdf:nil, so the cell's rdf:first and its closing rdf:rest both
        // have that one token as the object they spell.
        const string rdf = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";
        var bytes = Encoding.UTF8.GetBytes($"@prefix rdf: <{rdf}> .\r\n@prefix ex: <http://example.org/> .\r\nex:s ex:p ( rdf:nil ) .\r\n");

        // Act.
        var reading = Read(bytes);

        // Assert.
        var nils = reading.Elements.Where(element => element.Type == "Triple" && (string?)element.Attributes["o"] == rdf + "nil").ToList();
        Assert.Equal([rdf + "first", rdf + "rest"], nils.Select(element => (string)element.Attributes["p"]!).Order(StringComparer.Ordinal));
        Assert.All(nils, element => Assert.DoesNotContain("o", element.ValueSpans.Keys));
    }

    [Fact]
    public void ABodyThatDoesNotParse_IsUnreadable_WithTodaysOneFindingOnTheParsersLine()
    {
        // Arrange.
        var bytes = Fixture("broken.ttl");

        // Act.
        var reading = Read(bytes);

        // Assert.
        Assert.True(reading.Unreadable);
        Assert.Empty(reading.Elements);
        var finding = Assert.Single(reading.Findings);
        Assert.Equal(("rdf.unparseable", FindingSeverity.Error, 2), (finding.Code, finding.Severity, finding.Location!.Line));
        Assert.Equal("This is not RDF that can be read: Expected an object, found ''.", finding.Message);
    }

    [Fact]
    public void AnUnreadableBody_IsReadOnly_AndRefusesAChangeInTodaysSentence()
    {
        // Arrange.
        var broken = RdfBody.Open(Fixture("broken.ttl"), "broken.ttl");
        var readable = RdfBody.Open(Fixture("simple.nt"), "simple.nt");

        // Act.
        var refused = broken.Plan(new ModelChange.Remove("triple#0"));
        var planned = readable.Plan(new ModelChange.Remove("triple#0"));

        // Assert.
        Assert.True(broken.IsReadOnly);
        Assert.Equal("broken.ttl could not be read, so nothing can be edited until it can: it does not parse at line 2. Expected an object, found ''.", Assert.IsType<PlanResult.Refused>(refused).Reason);
        Assert.False(readable.IsReadOnly);
        Assert.Equal(RdfTurtlePlugin.PlanRefusal, Assert.IsType<PlanResult.Refused>(planned).Reason);
        Assert.Equal(RdfSyntax.NTriples, readable.Syntax);
    }

    [Fact]
    public void ThePlan_RefusesEveryChange_InItsOneSentence()
    {
        // Arrange.
        var plugin = new RdfTurtlePlugin(RdfSyntax.NTriples);
        var bytes = Fixture("simple.nt");
        var last = plugin.Read(new PluginReadRequest([new PluginFile("", bytes)]));

        // Act.
        var plan = plugin.Plan(new PluginPlanRequest([new PluginFile("", bytes)], last, new ModelChange.Remove("triple#0")));

        // Assert.
        Assert.Equal(RdfTurtlePlugin.PlanRefusal, Assert.IsType<PluginPlanResult.Refused>(plan).Reason);
        Assert.Equal(RdfSyntax.NTriples, plugin.Syntax);
    }

    [Fact]
    public void TheTemplate_IsTheRdfGraphsStarter()
    {
        // Act.
        var template = new RdfTurtlePlugin().Template(new PluginTemplateRequest("graph.ttl", new Dictionary<string, string>()));

        // Assert.
        Assert.Equal(RdfDefinitions.NewDocument(ServiceCollectionAddRdfExtension.RdfOrigin, "graph"), Encoding.UTF8.GetString(template));
    }

    [Fact]
    public void ABodyACommandsWriterChanged_IsReadAgain()
    {
        // Arrange: a body whose reading was asked for before the writer splices it.
        var body = RdfBody.Parse("@prefix ex: <http://example.org/> .\r\nex:a ex:p ex:b .\r\n");
        Assert.Single(body.Reading.Elements, element => element.Type == "Triple");
        var handler = new WritingHandler();

        // Act.
        var outcome = handler.Run(body, new AddRdfTripleCommand("graph.ttl", "http://example.org/a", "http://example.org/q", "http://example.org/c"));

        // Assert.
        Assert.True(outcome.WasApplied, outcome.Refusal);
        Assert.Equal(2, body.Reading.Elements.Count(element => element.Type == "Triple"));
        Assert.Equal(body.Text, Encoding.UTF8.GetString(body.Bytes));
    }

    /// <summary>The handler of a triple's addition, run on a body of the test's own, as the shared edit run hands it its copy.</summary>
    private sealed class WritingHandler() : RdfEditHandler<AddRdfTripleCommand>(new RdfDocumentStore(new FileWriter()))
    {
        public EditOutcome Run(RdfBody body, AddRdfTripleCommand command) => Apply(body, Parse(body), command);

        protected override EditOutcome Apply(LineDocument body, RdfModel model, AddRdfTripleCommand command) =>
            Outcome(RdfWriter.AddTriple(body, model, command.SubjectIri, command.PredicateIri, new IriTerm(command.ObjectIri, "")));
    }

    [Fact]
    public void TheCelFunctions_ReadTheDiagramsPrefixes_AsTheModulesOwnFunctionsDo()
    {
        // Arrange.
        var body = RdfBody.Open(Fixture("shared-statement.ttl"), "shared-statement.ttl");
        var model = RdfParser.Parse(body.Lines);
        var diagram = DislModelBuilder.From(body.Reading, RdfDefinitions.For(ServiceCollectionAddRdfExtension.RdfOrigin).Specification).Diagram;
        string[] iris = ["http://example.org/alice", "http://www.w3.org/2000/01/rdf-schema#label", "http://elsewhere.org/a/b#c", "http://elsewhere.org/a/", "urn:x"];
        string[] terms = ["ex:alice", "rdfs:label", "<urn:x>", "http://a.org/b", "nope:x", "bare", " "];

        // Act & assert: the definition's derived display reads the plugin's function, not its fallback.
        Assert.Equal("ex:alice", diagram.ElementById("res:http://example.org/alice")!.ValueOf("display"));
        Assert.Equal(iris.Select(iri => RdfProjection.Display(model, new IriTerm(iri, ""))), iris.Select(iri => RdfTurtlePlugin.DisplayName(diagram, iri)));
        Assert.Equal(terms.Select(term => RdfTermInput.Resolve(model, term).Iri ?? ""), terms.Select(term => RdfTurtlePlugin.ResolveTerm(diagram, term)));
        Assert.Equal(["alice", "label", "c", "", "urn:x"], iris.Select(RdfTurtlePlugin.LocalName));
    }
}
