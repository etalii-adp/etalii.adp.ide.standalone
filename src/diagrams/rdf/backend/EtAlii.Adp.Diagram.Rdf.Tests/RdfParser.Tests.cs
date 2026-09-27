using EtAlii.Adp.Documents;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.Rdf.Tests;

/// <summary>
/// The parser over Requirement 1.1's construct list, and the span discipline the writers splice
/// by: the line range each triple occupies and, on a shared line, the exact character fragment.
/// </summary>
public class RdfParserTests
{
    private static RdfModel Parse(string text) => RdfParser.Parse(LineDocument.Parse(text));

    private static RdfModel ParseFixture(string name)
    {
        var text = File.ReadAllText(IoPath.Combine(AppContext.BaseDirectory, "Fixtures", name));
        return Parse(text);
    }

    [Fact]
    public void TheConstructCorpus_ParsesWithEveryConstructAccountedFor()
    {
        // Arrange & act.
        var model = ParseFixture("constructs.ttl");

        // Assert.
        // The three declared prefixes, in order, and the base.
        Assert.Equal(["ex", "foaf", "dc"], model.Prefixes.Select(p => p.Prefix));
        Assert.Equal("http://example.org/base/", model.BaseIri);

        // 'a' became rdf:type without losing its spelling.
        var type = model.Triples.First(t => t.Predicate.Iri == RdfVocabulary.Type);
        Assert.Equal("a", type.Predicate.AsWritten);
        Assert.Equal("http://xmlns.com/foaf/0.1/Person", Assert.IsType<IriTerm>(type.Object).Iri);

        // The object list stated two knows edges from one pair.
        var alice = new IriTerm("http://example.org/alice", "ex:alice");
        var knows = model.Triples.Where(t => t.Subject.Equals(alice) && t.Predicate.Iri == "http://xmlns.com/foaf/0.1/knows").ToList();
        Assert.Equal(2, knows.Count);

        // Typed, language-tagged and bare numeric literals carry their datatypes.
        var age = model.Triples.First(t => t.Predicate.Iri == "http://xmlns.com/foaf/0.1/age");
        Assert.Equal(RdfVocabulary.XsdInteger, Assert.IsType<LiteralTerm>(age.Object).DatatypeIri);
        var score = model.Triples.First(t => t.Predicate.Iri == "http://xmlns.com/foaf/0.1/score");
        Assert.Equal(RdfVocabulary.XsdDouble, Assert.IsType<LiteralTerm>(score.Object).DatatypeIri);
        var description = model.Triples.First(t => t.Predicate.Iri == "http://purl.org/dc/elements/1.1/description");
        var tagged = Assert.IsType<LiteralTerm>(description.Object);
        Assert.Equal("en", tagged.Language);
        Assert.Equal("A description\nwith a break", tagged.Lexical);

        // The labeled blank node is one term across its uses.
        var carol = model.Triples.Where(t => t.Subject is BlankTerm { Label: "c" }).ToList();
        Assert.Equal(2, carol.Count);
        Assert.Equal(carol[0].Subject, carol[1].Subject);

        // The collection expanded to its cons pairs, ending at nil.
        Assert.Contains(model.Triples, t => t.Predicate.Iri == RdfVocabulary.First);
        Assert.Contains(model.Triples, t => t.Predicate.Iri == RdfVocabulary.Rest && t.Object is IriTerm { Iri: RdfVocabulary.Nil });

        // The anonymous property list stated its triples under a fresh blank subject.
        var dave = model.Triples.First(t => t.Object is LiteralTerm { Lexical: "Dave" });
        Assert.IsType<BlankTerm>(dave.Subject);

        // Relative IRIs resolved against the base; '<>' is the base itself.
        Assert.Contains(model.Triples, t => t.Subject is IriTerm { Iri: "http://example.org/base/relative" });
        Assert.Contains(model.Triples, t => t.Object is IriTerm { Iri: "http://example.org/base/" });

        // The long string kept its inner quotes and its line break - the CRLF the fixture's
        // bytes actually hold, because the lexical value is what the file says, not a tidied form.
        var title = model.Triples.First(t => t.Predicate.Iri == "http://purl.org/dc/elements/1.1/title");
        Assert.Equal("A long\r\nstring with \"quotes\" inside", Assert.IsType<LiteralTerm>(title.Object).Lexical);

        // The boolean keyword literal.
        Assert.Contains(model.Triples, t => t.Object is LiteralTerm { Lexical: "true", DatatypeIri: RdfVocabulary.XsdBoolean });
    }

    [Fact]
    public void NTriples_ParseThroughTheSameDoor()
    {
        // Arrange & act.
        var model = ParseFixture("simple.nt");

        // Assert.
        Assert.Equal(4, model.Triples.Count);
        // One triple per line: each statement is a single line, and each triple's own tokens
        // share that line with their subject - so the fragment is set, never the whole line.
        Assert.All(model.Triples, t =>
        {
            Assert.Equal(t.Span.StartLine, t.Span.EndLine);
            Assert.Equal(new LineRange(t.Span.StartLine, t.Span.StartLine), t.Statement);
            Assert.False(t.Span.OwnsLines);
        });
        Assert.Contains(model.Triples, t => t.Subject is BlankTerm { Label: "b0" });
    }

    [Fact]
    public void ASingleLineStatement_RecordsItsFragmentBesideItsSubject()
    {
        // Arrange & act.
        var model = Parse("@prefix ex: <http://example.org/> .\r\nex:s ex:p ex:o .\r\n");

        // Assert.
        var triple = Assert.Single(model.Triples);
        // The subject shares the line, so the triple's own tokens are a fragment of it.
        Assert.Equal(1, triple.Span.StartLine);
        Assert.Equal(1, triple.Span.EndLine);
        Assert.Equal(5, triple.Span.FragmentStart);
        Assert.Equal(14, triple.Span.FragmentEnd);
        Assert.Equal(new LineRange(1, 1), triple.Statement);
    }

    [Fact]
    public void APredicateListContinuationOnItsOwnLine_OwnsThatLine()
    {
        // Arrange & act.
        var model = Parse("@prefix ex: <http://example.org/> .\r\nex:s ex:p1 ex:o1 ;\r\n     ex:p2 ex:o2 .\r\n");

        // Assert.
        Assert.Equal(2, model.Triples.Count);

        // The first pair shares its line with the subject: a fragment.
        Assert.False(model.Triples[0].Span.OwnsLines);
        Assert.Equal(5, model.Triples[0].Span.FragmentStart);

        // The continuation's line holds nothing else but the terminator: it owns the line.
        Assert.True(model.Triples[1].Span.OwnsLines);
        Assert.Equal(2, model.Triples[1].Span.StartLine);
        Assert.Equal(2, model.Triples[1].Span.EndLine);

        // Both triples know the whole statement's extent, subject through dot.
        Assert.Equal(new LineRange(1, 2), model.Triples[0].Statement);
        Assert.Equal(new LineRange(1, 2), model.Triples[1].Statement);
    }

    [Fact]
    public void AnObjectListOnOneLine_GivesEachTripleItsOwnFragment()
    {
        // Arrange & act.
        var model = Parse("@prefix ex: <http://example.org/> .\r\nex:s ex:p ex:o1, ex:o2 .\r\n");

        // Assert.
        Assert.Equal(2, model.Triples.Count);

        // The first triple's fragment runs from the predicate through its object.
        Assert.Equal((5, 15), (model.Triples[0].Span.FragmentStart, model.Triples[0].Span.FragmentEnd));

        // The continuation's fragment is its object alone.
        Assert.Equal((17, 22), (model.Triples[1].Span.FragmentStart, model.Triples[1].Span.FragmentEnd));
    }

    [Fact]
    public void ATripleSpanningLines_CarriesNoFragment()
    {
        // Arrange & act.
        var model = Parse("@prefix ex: <http://example.org/> .\r\nex:s ex:p \"\"\"one\r\ntwo\"\"\" .\r\n");

        // Assert.
        var triple = Assert.Single(model.Triples);
        Assert.Equal(1, triple.Span.StartLine);
        Assert.Equal(2, triple.Span.EndLine);
        Assert.True(triple.Span.OwnsLines);
    }

    [Fact]
    public void BlankNodeOrdinals_AreDeterministicAcrossParses()
    {
        // Arrange.
        var text = File.ReadAllText(IoPath.Combine(AppContext.BaseDirectory, "Fixtures", "constructs.ttl"));

        // Act.
        var first = Parse(text);
        var second = Parse(text);

        // Assert.
        var firstBlanks = first.Triples.Select(t => t.Subject).OfType<BlankTerm>().Select(b => (b.Label, b.Ordinal));
        var secondBlanks = second.Triples.Select(t => t.Subject).OfType<BlankTerm>().Select(b => (b.Label, b.Ordinal));
        Assert.Equal(firstBlanks, secondBlanks);
    }

    [Fact]
    public void AnUndeclaredPrefix_IsRefusedNamingItsLine()
    {
        // Arrange & act.
        var exception = Assert.Throws<RdfParseException>(() => Parse("@prefix ex: <http://example.org/> .\r\nex:s nope:p ex:o .\r\n"));

        // Assert.
        Assert.Equal(2, exception.Line);
        Assert.Contains("nope", exception.Message);
    }

    [Fact]
    public void SparqlStyleDirectives_WorkWithoutTheirDot()
    {
        // Arrange & act.
        var model = Parse("PREFIX ex: <http://example.org/>\r\nBASE <http://example.org/base/>\r\nex:s ex:p <thing> .\r\n");

        // Assert.
        var triple = Assert.Single(model.Triples);
        Assert.Equal("http://example.org/base/thing", Assert.IsType<IriTerm>(triple.Object).Iri);
    }

    [Fact]
    public void WithoutABase_ARelativeIriStaysAsWritten()
    {
        // Arrange & act.
        var model = Parse("@prefix ex: <http://example.org/> .\r\nex:s ex:p <thing> .\r\n");

        // Assert.
        // Guessing a base would invent identity; the validator warns instead (Requirement 7.3).
        var triple = Assert.Single(model.Triples);
        Assert.Equal("thing", Assert.IsType<IriTerm>(triple.Object).Iri);
    }

    [Fact]
    public void ACollection_ExpandsToItsConsPairs()
    {
        // Arrange & act.
        var model = Parse("@prefix ex: <http://example.org/> .\r\nex:s ex:p ( ex:a ex:b ) .\r\n");

        // Assert.
        // Two firsts, one rest link, one nil terminator, and the statement's own triple.
        Assert.Equal(5, model.Triples.Count);
        Assert.Equal(2, model.Triples.Count(t => t.Predicate.Iri == RdfVocabulary.First));
        Assert.Equal(2, model.Triples.Count(t => t.Predicate.Iri == RdfVocabulary.Rest));
        Assert.Single(model.Triples, t => t.Object is IriTerm { Iri: RdfVocabulary.Nil });
    }
}
