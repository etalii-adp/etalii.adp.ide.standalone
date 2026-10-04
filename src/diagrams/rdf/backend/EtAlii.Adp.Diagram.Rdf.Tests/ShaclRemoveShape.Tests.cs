using EtAlii.Adp.Diagram.Rdf.Shacl;
using EtAlii.Adp.Documents;
using Xunit;

namespace EtAlii.Adp.Diagram.Rdf.Tests;

/// <summary>
/// The exclusive-reachability walk (shacl-diagram Requirement 5.6) - the riskiest operation in
/// this reading, so its "stays" cases assert the surviving text BYTE FOR BYTE rather than
/// counting triples: one triple too many is silent damage to a file ADP does not own, and a count
/// assertion is exactly the assertion that would not notice.
/// </summary>
public class ShaclRemoveShapeTests
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

    // Survivors are written out in full rather than derived from the input by dropping lines: a
    // line-dropping helper silently deletes the survivor's copy of a line the removed shape also
    // had, which is precisely the confusion these tests exist to catch.

    [Fact]
    public void AnInlineSubtree_GoesWithTheStatementThatHoldsIt()
    {
        // The form the world writes: the property shape lives inside the shape's own statement,
        // so nothing separate has to be found or removed.
        (LineDocument document, RdfModel model) = Open("""
                                                       ex:S a sh:NodeShape ;
                                                         sh:property [ sh:path ex:name ; sh:minCount 1 ] .
                                                       ex:Keep a sh:NodeShape ;
                                                         sh:property [ sh:path ex:other ] .
                                                       """ + "\n");

        var refusal = ShaclWriter.RemoveShapeWithSubtrees(document, model, Ex + "S");

        Assert.Equal("", refusal);
        Assert.Equal(
            Prelude + """
            ex:Keep a sh:NodeShape ;
              sh:property [ sh:path ex:other ] .
            """ + "\n",
            document.Text);
    }

    [Fact]
    public void AShapeWithSeveralPropertyBlocks_SweepsThemAll_WithoutStaleOffsets()
    {
        // The case a one-block shape cannot catch. Each removal shifts every offset after it, so
        // a walk that collected its victims from a single parse and then looped over that list
        // would splice the second one at a position that has moved - silently, no exception and
        // no refusal, just wrong bytes. Four blocks and a following statement that must survive
        // byte-identical is what proves the loop re-derives from a fresh parse each time.
        (LineDocument document, RdfModel model) = Open("""
                                                       ex:S a sh:NodeShape ;
                                                         sh:property [ sh:path ex:one ; sh:minCount 1 ] ;
                                                         sh:property [ sh:path ex:two ; sh:datatype xsd:string ] ;
                                                         sh:property [ sh:path ex:three ] ;
                                                         sh:property [ sh:path ex:four ; sh:maxCount 2 ] .
                                                       ex:Keep a sh:NodeShape ;
                                                         sh:property [ sh:path ex:kept ; sh:minCount 1 ] .
                                                       """ + "\n");

        var refusal = ShaclWriter.RemoveShapeWithSubtrees(document, model, Ex + "S");

        Assert.Equal("", refusal);
        Assert.Equal(
            Prelude + """
            ex:Keep a sh:NodeShape ;
              sh:property [ sh:path ex:kept ; sh:minCount 1 ] .
            """ + "\n",
            document.Text);
    }

    [Fact]
    public void SeveralSeparatelyStatedSubtrees_GoWithoutStaleOffsets()
    {
        // The same hazard on the other pass: four subtree statements removed in sequence, each
        // splice moving what follows it.
        (LineDocument document, RdfModel model) = Open("""
                                                       ex:S a sh:NodeShape ;
                                                         sh:property _:a ;
                                                         sh:property _:b ;
                                                         sh:property _:c .
                                                       _:a sh:path ex:one ; sh:minCount 1 .
                                                       _:b sh:path ex:two ; sh:datatype xsd:string .
                                                       _:c sh:path ex:three .
                                                       ex:Keep a sh:NodeShape .
                                                       """ + "\n");

        var refusal = ShaclWriter.RemoveShapeWithSubtrees(document, model, Ex + "S");

        Assert.Equal("", refusal);
        Assert.Equal(Prelude + "ex:Keep a sh:NodeShape .\n", document.Text);
    }

    [Fact]
    public void AnExclusiveSeparatelyStatedSubtree_Goes()
    {
        (LineDocument document, RdfModel model) = Open("""
                                                       ex:S a sh:NodeShape ;
                                                         sh:property _:row .
                                                       _:row sh:path ex:name ;
                                                         sh:minCount 1 .
                                                       ex:Keep a sh:NodeShape .
                                                       """ + "\n");

        var refusal = ShaclWriter.RemoveShapeWithSubtrees(document, model, Ex + "S");

        Assert.Equal("", refusal);
        Assert.Equal(Prelude + "ex:Keep a sh:NodeShape .\n", document.Text);
    }

    [Fact]
    public void ASubtreeASurvivingShapeAlsoReferences_StaysByteIdentical()
    {
        // The case that matters most: _:shared is reachable from ex:S, but ex:Keep names it too.
        // Removing it would damage a shape nobody asked to touch.
        const string body = """
            ex:S a sh:NodeShape ;
              sh:property _:shared .
            ex:Keep a sh:NodeShape ;
              sh:property _:shared .
            _:shared sh:path ex:name ;
              sh:minCount 1 .
            """ + "\n";
        (LineDocument document, RdfModel model) = Open(body);

        var refusal = ShaclWriter.RemoveShapeWithSubtrees(document, model, Ex + "S");

        Assert.Equal("", refusal);
        Assert.Equal(
            Prelude + """
            ex:Keep a sh:NodeShape ;
              sh:property _:shared .
            _:shared sh:path ex:name ;
              sh:minCount 1 .
            """ + "\n",
            document.Text);
    }

    [Fact]
    public void ADiamond_TwoPathsFromTheRemovedShapeAndNoOtherReferrer_GoesExactlyOnce()
    {
        (LineDocument document, RdfModel model) = Open("""
                                                       ex:S a sh:NodeShape ;
                                                         sh:property _:twice ;
                                                         sh:node _:twice .
                                                       _:twice sh:path ex:name .
                                                       ex:Keep a sh:NodeShape .
                                                       """ + "\n");

        var refusal = ShaclWriter.RemoveShapeWithSubtrees(document, model, Ex + "S");

        Assert.Equal("", refusal);
        Assert.Equal(Prelude + "ex:Keep a sh:NodeShape .\n", document.Text);
    }

    [Fact]
    public void ABlankANonShapeSubjectAlsoReferences_Stays()
    {
        // The referrer is not a shape at all - co-resident data. It still counts as an outside
        // referrer, because exclusivity is about the file, not about this reading's projection.
        const string body = """
            ex:S a sh:NodeShape ;
              sh:property _:b .
            _:b sh:path ex:name .
            ex:someData ex:mentions _:b .
            """ + "\n";
        (LineDocument document, RdfModel model) = Open(body);

        var refusal = ShaclWriter.RemoveShapeWithSubtrees(document, model, Ex + "S");

        Assert.Equal("", refusal);
        Assert.Equal(
            Prelude + """
            _:b sh:path ex:name .
            ex:someData ex:mentions _:b .
            """ + "\n",
            document.Text);
    }

    [Fact]
    public void ThreeDeepNesting_IsRemovedBottomUp()
    {
        (LineDocument document, RdfModel model) = Open("""
                                                       ex:S a sh:NodeShape ;
                                                         sh:node _:one .
                                                       _:one sh:node _:two .
                                                       _:two sh:node _:three .
                                                       _:three sh:path ex:deep .
                                                       ex:Keep a sh:NodeShape .
                                                       """ + "\n");

        var refusal = ShaclWriter.RemoveShapeWithSubtrees(document, model, Ex + "S");

        Assert.Equal("", refusal);
        Assert.Equal(Prelude + "ex:Keep a sh:NodeShape .\n", document.Text);
    }

    [Fact]
    public void ADeepSubtreeSharedFurtherDown_KeepsEverythingFromThatPointDown()
    {
        // _:one is exclusive, but _:two is also reached by a surviving shape - so _:two and the
        // _:three below it must both survive, byte-identical, while _:one goes.
        const string body = """
            ex:S a sh:NodeShape ;
              sh:node _:one .
            _:one sh:node _:two .
            _:two sh:node _:three .
            _:three sh:path ex:deep .
            ex:Keep a sh:NodeShape ;
              sh:node _:two .
            """ + "\n";
        (LineDocument document, RdfModel model) = Open(body);

        var refusal = ShaclWriter.RemoveShapeWithSubtrees(document, model, Ex + "S");

        Assert.Equal("", refusal);
        Assert.Equal(
            Prelude + """
            _:two sh:node _:three .
            _:three sh:path ex:deep .
            ex:Keep a sh:NodeShape ;
              sh:node _:two .
            """ + "\n",
            document.Text);
    }

    [Fact]
    public void TheCount_IsStatedBeforeAnythingRuns_AndMatchesWhatGoes()
    {
        const string body = """
            ex:S a sh:NodeShape ;
              sh:property _:row .
            _:row sh:path ex:name ;
              sh:minCount 1 .
            ex:Keep a sh:NodeShape .
            """ + "\n";
        (LineDocument document, RdfModel model) = Open(body);

        var announced = ShaclWriter.CountShapeRemoval(model, Ex + "S");
        var before = model.Triples.Count;

        Assert.Equal("", ShaclWriter.RemoveShapeWithSubtrees(document, model, Ex + "S"));

        // 2 of the shape's own + 2 of the exclusive subtree, and the file lost exactly that many.
        Assert.Equal(4, announced);
        Assert.Equal(before - announced, RdfParser.Parse(document).Triples.Count);
    }

    [Fact]
    public void TheCount_ExcludesASharedSubtree()
    {
        (_, RdfModel model) = Open("""
                                   ex:S a sh:NodeShape ;
                                     sh:property _:shared .
                                   ex:Keep a sh:NodeShape ;
                                     sh:property _:shared .
                                   _:shared sh:path ex:name ;
                                     sh:minCount 1 .
                                   """ + "\n");

        // Only the shape's own two triples; the shared subtree's two are not ours to take.
        Assert.Equal(2, ShaclWriter.CountShapeRemoval(model, Ex + "S"));
    }

    [Fact]
    public void RemovingRefusesBlankRootedAndUnknownShapes_LeavingTheFileUntouched()
    {
        (LineDocument document, RdfModel model) = Open("ex:S a sh:NodeShape .\n");
        var before = document.Text;

        Assert.Equal(ShaclRefusals.BlankRooted, ShaclWriter.RemoveShapeWithSubtrees(document, model, ""));
        Assert.Equal(ShaclRefusals.NoSuchShape, ShaclWriter.RemoveShapeWithSubtrees(document, model, Ex + "Missing"));
        Assert.Equal(before, document.Text);
    }

    [Fact]
    public void AnInlineSubtreeSharedByLabel_StaysWhenAnotherShapeNamesIt()
    {
        // Mixed form: the subtree is written inline under ex:Keep and referenced by label from
        // ex:S. Removing ex:S must not reach into ex:Keep's statement.
        const string body = """
            ex:Keep a sh:NodeShape ;
              sh:property _:shared .
            _:shared sh:path ex:name .
            ex:S a sh:NodeShape ;
              sh:property _:shared .
            """ + "\n";
        (LineDocument document, RdfModel model) = Open(body);

        Assert.Equal("", ShaclWriter.RemoveShapeWithSubtrees(document, model, Ex + "S"));

        Assert.Equal(
            Prelude + """
            ex:Keep a sh:NodeShape ;
              sh:property _:shared .
            _:shared sh:path ex:name .
            """ + "\n",
            document.Text);
    }
}
