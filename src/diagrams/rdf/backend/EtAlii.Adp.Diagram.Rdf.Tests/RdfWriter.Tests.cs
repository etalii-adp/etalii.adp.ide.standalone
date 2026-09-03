using Xunit;

namespace EtAlii.Adp.Diagram.Rdf.Tests;

/// <summary>
/// The writer's named operations as smallest diffs: comma-list members leaving their neighbours
/// byte-untouched, semicolon pairs taking exactly one separator, last triples taking their block,
/// renames stranding no reference, literal rewrites touching no neighbouring byte - and refusals
/// landing before any splice (rdf-diagram Requirement 5).
/// </summary>
public class RdfWriterTests
{
    private const string Corpus =
        "@prefix ex: <http://example.org/> .\r\n"
        + "\r\n"
        + "ex:alice ex:knows ex:bob, ex:carol ;\r\n"
        + "    ex:name \"Alice\" .\r\n"
        + "\r\n"
        + "ex:bob ex:name \"Bob\" .\r\n";

    private static (RdfDocument Document, RdfModel Model) Load(string text)
    {
        var document = RdfDocument.Parse(text);
        return (document, RdfParser.Parse(document));
    }

    private static RdfTriple Find(RdfModel model, string predicateIri, string objectIri) =>
        model.Triples.Single(t => t.Predicate.Iri == predicateIri && t.Object is IriTerm o && o.Iri == objectIri);

    [Fact]
    public void RemovingACommaListContinuation_TakesTheObjectAndItsPrecedingComma_AndNothingElse()
    {
        // Arrange.
        var (document, model) = Load(Corpus);
        var triple = Find(model, "http://example.org/knows", "http://example.org/carol");

        // Act.
        var refusal = RdfWriter.RemoveTriple(document, model, triple);

        // Assert.
        Assert.Equal("", refusal);
        Assert.Equal(
            "@prefix ex: <http://example.org/> .\r\n"
            + "\r\n"
            + "ex:alice ex:knows ex:bob ;\r\n"
            + "    ex:name \"Alice\" .\r\n"
            + "\r\n"
            + "ex:bob ex:name \"Bob\" .\r\n",
            document.Text);
    }

    [Fact]
    public void RemovingACommaListHead_TakesTheObjectAndItsFollowingComma_LeavingThePredicateForTheRest()
    {
        // Arrange.
        var (document, model) = Load(Corpus);
        var triple = Find(model, "http://example.org/knows", "http://example.org/bob");

        // Act.
        var refusal = RdfWriter.RemoveTriple(document, model, triple);

        // Assert.
        Assert.Equal("", refusal);
        Assert.Equal(
            "@prefix ex: <http://example.org/> .\r\n"
            + "\r\n"
            + "ex:alice ex:knows ex:carol ;\r\n"
            + "    ex:name \"Alice\" .\r\n"
            + "\r\n"
            + "ex:bob ex:name \"Bob\" .\r\n",
            document.Text);
    }

    [Fact]
    public void RemovingASemicolonPair_TakesExactlyOneSeparator_AndTheTerminatorSurvives()
    {
        // Arrange.
        var (document, model) = Load(Corpus);
        var triple = model.Triples.Single(t =>
            t.Subject is IriTerm { Iri: "http://example.org/alice" } && t.Object is LiteralTerm { Lexical: "Alice" });

        // Act.
        var refusal = RdfWriter.RemoveTriple(document, model, triple);

        // Assert.
        Assert.Equal("", refusal);
        Assert.Equal(
            "@prefix ex: <http://example.org/> .\r\n"
            + "\r\n"
            + "ex:alice ex:knows ex:bob, ex:carol .\r\n"
            + "\r\n"
            + "ex:bob ex:name \"Bob\" .\r\n",
            document.Text);
    }

    [Fact]
    public void RemovingAStatementsOnlyTriple_TakesTheWholeBlockAndItsDot()
    {
        // Arrange.
        var (document, model) = Load(Corpus);
        var triple = model.Triples.Single(t =>
            t.Subject is IriTerm { Iri: "http://example.org/bob" } && t.Object is LiteralTerm { Lexical: "Bob" });

        // Act.
        var refusal = RdfWriter.RemoveTriple(document, model, triple);

        // Assert.
        Assert.Equal("", refusal);
        Assert.Equal(
            "@prefix ex: <http://example.org/> .\r\n"
            + "\r\n"
            + "ex:alice ex:knows ex:bob, ex:carol ;\r\n"
            + "    ex:name \"Alice\" .\r\n"
            + "\r\n",
            document.Text);
    }

    [Fact]
    public void RemovingAResource_TakesEveryTripleItTouches_BottomUp()
    {
        // Arrange.
        var (document, model) = Load(Corpus);

        // Act.
        var refusal = RdfWriter.RemoveResource(document, model, "http://example.org/bob");

        // Assert.
        Assert.Equal("", refusal);
        Assert.Equal(
            "@prefix ex: <http://example.org/> .\r\n"
            + "\r\n"
            + "ex:alice ex:knows ex:carol ;\r\n"
            + "    ex:name \"Alice\" .\r\n"
            + "\r\n",
            document.Text);
    }

    [Fact]
    public void RenamingATerm_RewritesEveryOccurrence_StrandingNoReference()
    {
        // Arrange.
        var (document, model) = Load(Corpus);

        // Act.
        var refusal = RdfWriter.RenameTerm(document, model, "http://example.org/bob", "http://example.org/robert");

        // Assert.
        Assert.Equal("", refusal);
        Assert.Equal(
            "@prefix ex: <http://example.org/> .\r\n"
            + "\r\n"
            + "ex:alice ex:knows ex:robert, ex:carol ;\r\n"
            + "    ex:name \"Alice\" .\r\n"
            + "\r\n"
            + "ex:robert ex:name \"Bob\" .\r\n",
            document.Text);
    }

    [Fact]
    public void RenamingOntoAnExistingTerm_IsRefusedBeforeAnySplice()
    {
        // Arrange.
        var (document, model) = Load(Corpus);

        // Act.
        var refusal = RdfWriter.RenameTerm(document, model, "http://example.org/bob", "http://example.org/alice");

        // Assert.
        Assert.Contains("already names", refusal);
        Assert.Equal(Corpus, document.Text);
    }

    [Fact]
    public void ReplacingALiteralObject_TouchesNoNeighbouringByte()
    {
        // Arrange.
        var (document, model) = Load(Corpus);
        var triple = model.Triples.Single(t =>
            t.Subject is IriTerm { Iri: "http://example.org/alice" } && t.Object is LiteralTerm);

        // Act.
        var refusal = RdfWriter.ReplaceObjectLiteral(document, model, triple, "Alicia", "en", null);

        // Assert.
        Assert.Equal("", refusal);
        Assert.Equal(
            "@prefix ex: <http://example.org/> .\r\n"
            + "\r\n"
            + "ex:alice ex:knows ex:bob, ex:carol ;\r\n"
            + "    ex:name \"Alicia\"@en .\r\n"
            + "\r\n"
            + "ex:bob ex:name \"Bob\" .\r\n",
            document.Text);
    }

    [Fact]
    public void ReplacingANonLiteralObject_IsRefused()
    {
        // Arrange.
        var (document, model) = Load(Corpus);
        var triple = Find(model, "http://example.org/knows", "http://example.org/bob");

        // Act.
        var refusal = RdfWriter.ReplaceObjectLiteral(document, model, triple, "nope", null, null);

        // Assert.
        Assert.Contains("not a literal", refusal);
        Assert.Equal(Corpus, document.Text);
    }

    [Fact]
    public void AddingATripleToAnExistingSubject_ContinuesItsBlockWithItsIndentation()
    {
        // Arrange.
        var (document, model) = Load(Corpus);

        // Act.
        var refusal = RdfWriter.AddTriple(
            document, model, "http://example.org/bob", "http://example.org/knows",
            new IriTerm("http://example.org/alice", "ex:alice"));

        // Assert.
        Assert.Equal("", refusal);
        Assert.Equal(
            "@prefix ex: <http://example.org/> .\r\n"
            + "\r\n"
            + "ex:alice ex:knows ex:bob, ex:carol ;\r\n"
            + "    ex:name \"Alice\" .\r\n"
            + "\r\n"
            + "ex:bob ex:name \"Bob\" ;\r\n"
            + "    ex:knows ex:alice .\r\n",
            document.Text);
    }

    [Fact]
    public void AddingATripleForANewSubject_AppendsAStatement_WithDeclaredPrefixesNeverInvented()
    {
        // Arrange.
        var (document, model) = Load(Corpus);

        // Act.
        // The predicate's namespace is not declared, so it must be written in full, not invented.
        var refusal = RdfWriter.AddTriple(
            document, model, "http://example.org/dave", "http://xmlns.com/foaf/0.1/name",
            new LiteralTerm("Dave", null, null, "\"Dave\""));

        // Assert.
        Assert.Equal("", refusal);
        Assert.Equal(
            Corpus
            + "\r\n"
            + "ex:dave <http://xmlns.com/foaf/0.1/name> \"Dave\" .\r\n",
            document.Text);
    }

    [Fact]
    public void AddingAPrefix_LandsBesideTheDeclarationRun_AndRedeclarationIsRefused()
    {
        // Arrange.
        var (document, model) = Load(Corpus);

        // Act.
        var added = RdfWriter.AddPrefix(document, model, "foaf", "http://xmlns.com/foaf/0.1/");
        var redeclared = RdfWriter.AddPrefix(document, model, "ex", "http://elsewhere.example/");

        // Assert.
        Assert.Equal("", added);
        Assert.Contains("already declared", redeclared);
        Assert.StartsWith(
            "@prefix ex: <http://example.org/> .\r\n"
            + "@prefix foaf: <http://xmlns.com/foaf/0.1/> .\r\n",
            document.Text);
    }

    [Fact]
    public void BlankNodeRootedEdits_AreRefusedWithTheIdentityBoundarySentence()
    {
        // Arrange.
        var (document, model) = Load(
            "@prefix ex: <http://example.org/> .\r\n"
            + "_:someone ex:name \"Nobody\" .\r\n");
        var triple = Assert.Single(model.Triples);

        // Act.
        var removeTriple = RdfWriter.RemoveTriple(document, model, triple);
        var addBlank = RdfWriter.AddTriple(document, model, "http://example.org/alice", "http://example.org/knows", new BlankTerm(null, 99));

        // Assert.
        Assert.Contains("blank node", removeTriple);
        Assert.Contains("blank node", addBlank);
        Assert.Equal("@prefix ex: <http://example.org/> .\r\n_:someone ex:name \"Nobody\" .\r\n", document.Text);
    }

    [Fact]
    public void NTriples_FlowThroughTheSameOperationsDegenerately()
    {
        // Arrange.
        var (document, model) = Load(
            "<http://example.org/a> <http://example.org/p> <http://example.org/b> .\n"
            + "<http://example.org/a> <http://example.org/q> \"x\" .\n"
            + "<http://example.org/c> <http://example.org/p> <http://example.org/a> .\n");
        var triple = model.Triples.Single(t => t.Object is LiteralTerm);

        // Act.
        var refusal = RdfWriter.RemoveTriple(document, model, triple);

        // Assert.
        // Every triple owns exactly one line, so the removal is a whole-line removal and the
        // neighbouring lines are byte-untouched - LF endings included.
        Assert.Equal("", refusal);
        Assert.Equal(
            "<http://example.org/a> <http://example.org/p> <http://example.org/b> .\n"
            + "<http://example.org/c> <http://example.org/p> <http://example.org/a> .\n",
            document.Text);
    }

    [Fact]
    public void EveryRefusal_LeavesTheDocumentByteIdentical()
    {
        // Arrange.
        var (document, model) = Load(Corpus);

        // Act.
        var missing = RdfWriter.RemoveResource(document, model, "http://example.org/nobody");
        var sameName = RdfWriter.RenameTerm(document, model, "http://example.org/bob", "http://example.org/bob");

        // Assert.
        Assert.Contains("nothing to remove", missing);
        Assert.Contains("nothing to rename", sameName);
        Assert.Equal(Corpus, document.Text);
    }
}
