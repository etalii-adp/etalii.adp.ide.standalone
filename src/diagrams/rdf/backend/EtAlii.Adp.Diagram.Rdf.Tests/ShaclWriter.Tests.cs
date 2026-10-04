using EtAlii.Adp.Diagram.Rdf.Shacl;
using EtAlii.Adp.Documents;
using Xunit;

namespace EtAlii.Adp.Diagram.Rdf.Tests;

/// <summary>
/// This reading's writers: the one SHACL-owned splice that adds a property row, and the gestures
/// that dispatch through the family writer rather than splicing privately. Every assertion is a
/// smallest-diff assertion - what changed, and that nothing else did
/// (shacl-diagram Requirements 5.1-5.5).
/// </summary>
public class ShaclWriterTests
{
    private const string Prelude = """
        @prefix sh: <http://www.w3.org/ns/shacl#> .
        @prefix ex: <http://example.org/> .
        @prefix xsd: <http://www.w3.org/2001/XMLSchema#> .

        """;

    private const string Ex = "http://example.org/";

    private static (LineDocument Document, RdfModel Model) Open(string body)
    {
        var document = LineDocument.Parse(Prelude + body);
        return (document, RdfParser.Parse(document));
    }

    [Fact]
    public void AppendPropertyShapeBlock_SplicesOneContinuation_MatchingTheBlocksIndentation()
    {
        // Arrange.
        (LineDocument document, RdfModel model) = Open("""
                                                       ex:PersonShape a sh:NodeShape ;
                                                         sh:targetClass ex:Person .
                                                       """ + "\r\n");

        // Act.
        var refusal = ShaclWriter.AppendPropertyShapeBlock(
            document, model, Ex + "PersonShape", Ex + "name",
            new ShaclPropertyShapeOptions(DatatypeIri: "http://www.w3.org/2001/XMLSchema#string", MinCount: 1, MaxCount: 1, Name: "name"));

        // Assert: the terminator became a ';', the row landed on its own line at the block's
        // indentation, prefixes reused throughout, and the prelude is untouched.
        Assert.Equal("", refusal);
        Assert.Equal(
            Prelude + """
            ex:PersonShape a sh:NodeShape ;
              sh:targetClass ex:Person ;
              sh:property [ sh:path ex:name ; sh:datatype xsd:string ; sh:minCount 1 ; sh:maxCount 1 ; sh:name "name" ] .
            """ + "\r\n",
            document.Text);
    }

    [Fact]
    public void AppendPropertyShapeBlock_OnASingleLineStatement_IndentsOneStepIn()
    {
        (LineDocument document, RdfModel model) = Open("ex:S a sh:NodeShape .\r\n");

        var refusal = ShaclWriter.AppendPropertyShapeBlock(document, model, Ex + "S", Ex + "p");

        Assert.Equal("", refusal);
        Assert.Equal(
            Prelude + "ex:S a sh:NodeShape ;\r\n    sh:property [ sh:path ex:p ] .\r\n",
            document.Text);
    }

    [Fact]
    public void AppendPropertyShapeBlock_LeavesNeighbouringStatementsByteIdentical()
    {
        (LineDocument document, RdfModel model) = Open("""
                                                       ex:Other a sh:NodeShape ; sh:closed true .
                                                       ex:S a sh:NodeShape .
                                                       ex:Third a sh:NodeShape ; sh:targetNode ex:x .
                                                       """ + "\r\n");
        var before = document.Text;

        ShaclWriter.AppendPropertyShapeBlock(document, model, Ex + "S", Ex + "p");

        var beforeLines = before.Split('\n');
        var afterLines = document.Text.Split('\n');
        Assert.Equal(beforeLines[3], afterLines[3]); // ex:Other, untouched
        Assert.Equal(beforeLines[5], afterLines[6]); // ex:Third, shifted by the insert, byte-equal
    }

    [Fact]
    public void AppendPropertyShapeBlock_NeverInventsAPrefix()
    {
        // ex: is declared, foaf: is not - so foaf's IRI is written in full brackets rather than
        // gaining a declaration as a side effect.
        (LineDocument document, RdfModel model) = Open("ex:S a sh:NodeShape .\r\n");

        ShaclWriter.AppendPropertyShapeBlock(document, model, Ex + "S", "http://xmlns.com/foaf/0.1/name");

        Assert.Contains("sh:path <http://xmlns.com/foaf/0.1/name>", document.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("@prefix foaf:", document.Text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("", "http://example.org/p")]
    [InlineData("http://example.org/S", "")]
    public void AppendPropertyShapeBlock_RefusesBeforeAnySplice(string shapeIri, string pathIri)
    {
        (LineDocument document, RdfModel model) = Open("ex:S a sh:NodeShape .\r\n");
        var before = document.Text;

        var refusal = ShaclWriter.AppendPropertyShapeBlock(document, model, shapeIri, pathIri);

        Assert.NotEqual("", refusal);
        Assert.Equal(before, document.Text); // untouched to the byte
    }

    [Fact]
    public void AppendPropertyShapeBlock_RefusesAnUnknownShape()
    {
        (LineDocument document, RdfModel model) = Open("ex:S a sh:NodeShape .\r\n");
        var before = document.Text;

        var refusal = ShaclWriter.AppendPropertyShapeBlock(document, model, Ex + "Missing", Ex + "p");

        Assert.Equal(ShaclRefusals.NoSuchShape, refusal);
        Assert.Equal(before, document.Text);
    }

    [Fact]
    public void AddTarget_AndRemoveTarget_RoundTripThroughTheFamilyWriter()
    {
        (LineDocument document, RdfModel model) = Open("ex:S a sh:NodeShape .\r\n");
        var before = document.Text;

        Assert.Equal("", ShaclWriter.AddTarget(document, model, Ex + "S", ShaclVocabulary.TargetClass, new IriTerm(Ex + "Person", "ex:Person")));
        Assert.Contains("sh:targetClass ex:Person", document.Text, StringComparison.Ordinal);

        // Removal addresses the chip's own address triple - shape, predicate, term.
        var reparsed = RdfParser.Parse(document);
        Assert.Equal("", ShaclWriter.RemoveTarget(document, reparsed, Ex + "S", ShaclVocabulary.TargetClass, Ex + "Person"));
        Assert.Equal(before, document.Text);
    }

    [Fact]
    public void RemoveTarget_RefusesWhatIsNotThere_AndTheImplicitTarget()
    {
        (LineDocument document, RdfModel model) = Open("ex:S a sh:NodeShape .\r\n");
        var before = document.Text;

        Assert.Equal(ShaclRefusals.NoSuchTarget, ShaclWriter.RemoveTarget(document, model, Ex + "S", ShaclVocabulary.TargetClass, Ex + "Person"));

        // The implicit chip carries an empty predicate, because no triple states it.
        Assert.Equal(ShaclRefusals.ImplicitTarget, ShaclWriter.RemoveTarget(document, model, Ex + "S", "", Ex + "S"));
        Assert.Equal(before, document.Text);
    }

    [Fact]
    public void CreateNodeShape_WritesOneTypeTriple()
    {
        (LineDocument document, RdfModel model) = Open("ex:Existing a sh:NodeShape .\r\n");

        Assert.Equal("", ShaclWriter.CreateNodeShape(document, model, Ex + "Fresh"));

        Assert.Contains("ex:Fresh a sh:NodeShape .", document.Text, StringComparison.Ordinal);
        Assert.Contains("ex:Existing a sh:NodeShape .", document.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void SetDeactivated_AddsAndRemovesTheTriple_AndIsIdempotent()
    {
        (LineDocument document, RdfModel model) = Open("ex:S a sh:NodeShape .\r\n");
        var before = document.Text;

        Assert.Equal("", ShaclWriter.SetDeactivated(document, model, Ex + "S", deactivated: true));

        // Asserted by what the file now states rather than by its spelling: the family writer
        // emits the typed form ("true"^^xsd:boolean) where an author would more often write the
        // bare keyword, and both parse to the same literal the projection reads.
        Assert.Contains(
            RdfParser.Parse(document).Triples,
            triple => triple.Predicate.Iri == ShaclVocabulary.Deactivated && triple.Object is LiteralTerm { Lexical: "true" });

        // Asking again changes nothing rather than writing a duplicate.
        var afterFirst = document.Text;
        var reparsed = RdfParser.Parse(document);
        Assert.Equal("", ShaclWriter.SetDeactivated(document, reparsed, Ex + "S", deactivated: true));
        Assert.Equal(afterFirst, document.Text);

        reparsed = RdfParser.Parse(document);
        Assert.Equal("", ShaclWriter.SetDeactivated(document, reparsed, Ex + "S", deactivated: false));
        Assert.Equal(before, document.Text);
    }

    [Fact]
    public void SetLiteral_RewritesInPlaceThroughReplaceObjectLiteral_TouchingNoNeighbouringByte()
    {
        (LineDocument document, RdfModel model) = Open("""
                                                       ex:S a sh:NodeShape ;
                                                         sh:name "Old name" ;
                                                         sh:description "Kept exactly." .
                                                       """ + "\r\n");

        Assert.Equal("", ShaclWriter.SetLiteral(document, model, Ex + "S", ShaclVocabulary.Name, "New name"));

        Assert.Equal(
            Prelude + """
            ex:S a sh:NodeShape ;
              sh:name "New name" ;
              sh:description "Kept exactly." .
            """ + "\r\n",
            document.Text);
    }

    [Fact]
    public void SetLiteral_AddsTheTripleWhereNoneIsStated()
    {
        (LineDocument document, RdfModel model) = Open("ex:S a sh:NodeShape .\r\n");

        Assert.Equal("", ShaclWriter.SetLiteral(document, model, Ex + "S", ShaclVocabulary.Name, "Fresh"));

        Assert.Contains("sh:name \"Fresh\"", document.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryGesture_RefusesABlankRootedShape_WithTheOneBoundarySentence()
    {
        (LineDocument document, RdfModel model) = Open("[] a sh:NodeShape .\r\n");
        var before = document.Text;

        // An anonymous shape carries no IRI, so every gesture keyed to one refuses identically.
        Assert.Equal(ShaclRefusals.BlankRooted, ShaclWriter.AppendPropertyShapeBlock(document, model, "", Ex + "p"));
        Assert.Equal(ShaclRefusals.BlankRooted, ShaclWriter.AddTarget(document, model, "", ShaclVocabulary.TargetClass, new IriTerm(Ex + "C", "ex:C")));
        Assert.Equal(ShaclRefusals.BlankRooted, ShaclWriter.RemoveTarget(document, model, "", ShaclVocabulary.TargetClass, Ex + "C"));
        Assert.Equal(ShaclRefusals.BlankRooted, ShaclWriter.SetDeactivated(document, model, "", deactivated: true));
        Assert.Equal(ShaclRefusals.BlankRooted, ShaclWriter.SetLiteral(document, model, "", ShaclVocabulary.Name, "x"));
        Assert.Equal(before, document.Text);
    }
}
