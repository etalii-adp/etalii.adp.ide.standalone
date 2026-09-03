using EtAlii.Adp.Diagram.Rdf.Shacl;
using Xunit;

namespace EtAlii.Adp.Diagram.Rdf.Tests;

/// <summary>
/// The double refusal of shacl-diagram Requirement 3.3, tested as two independent defences plus
/// their agreement. A refusal implemented in one layer and assumed in the other is how such
/// refusals erode: the writer must refuse when called directly with no provider in sight, the
/// gate must refuse without consulting the writer, and both must say the same sentence so a user
/// who meets each reads one refusal rather than two that drifted apart.
/// </summary>
public class ShaclDoubleRefusalTests
{
    private const string Prelude = """
        @prefix sh: <http://www.w3.org/ns/shacl#> .
        @prefix ex: <http://example.org/> .

        """;

    private static (RdfDocument Document, RdfModel Model) Open(string body)
    {
        var document = RdfDocument.Parse(Prelude + body);
        return (document, RdfParser.Parse(document));
    }

    // ---- layer one: the writer, called directly, bypassing every provider -------------------

    [Fact]
    public void TheWriterAlone_RefusesEveryBlankRootedMutation_AndSplicesNothing()
    {
        var (document, model) = Open("[] a sh:NodeShape ; sh:property [ sh:path ex:p ] .\n");
        var before = document.Text;

        // No provider is involved: this is the writer's own guard, which is the point.
        Assert.Equal(ShaclRefusals.BlankRooted, ShaclWriter.AppendPropertyShapeBlock(document, model, "", "http://example.org/q"));
        Assert.Equal(ShaclRefusals.BlankRooted, ShaclWriter.RemoveShapeWithSubtrees(document, model, ""));
        Assert.Equal(ShaclRefusals.BlankRooted, ShaclWriter.SetDeactivated(document, model, "", deactivated: true));
        Assert.Equal(ShaclRefusals.BlankRooted, ShaclWriter.SetLiteral(document, model, "", ShaclVocabulary.Name, "x"));
        Assert.Equal(ShaclRefusals.BlankRooted, ShaclWriter.AddTarget(document, model, "", ShaclVocabulary.TargetClass, new IriTerm("http://example.org/C", "ex:C")));
        Assert.Equal(ShaclRefusals.BlankRooted, ShaclWriter.RemoveTarget(document, model, "", ShaclVocabulary.TargetClass, "http://example.org/C"));

        Assert.Equal(before, document.Text);
    }

    // ---- layer two: the gate, deciding without the writer ------------------------------------

    [Fact]
    public void TheGateAlone_RefusesAnAnonymousShape_WithoutTouchingTheWriterOrTheDocument()
    {
        var (document, model) = Open("[] a sh:NodeShape ; sh:targetClass ex:Person .\n");
        var before = document.Text;

        // The blank card's id, as the projection mints it.
        var blankId = ShaclProjection.Project(model).Cards.Single(card => card.Blank).Id;

        var decision = ShaclEditGate.For(model, blankId, truncated: false);

        Assert.True(decision.Applies);          // the reading does have something to say
        Assert.False(decision.Available);       // and what it says is no
        Assert.Equal(ShaclRefusals.BlankRooted, decision.Reason);
        Assert.Equal("", decision.ShapeIri);

        // Deciding cost the document nothing - no splice was attempted to find out.
        Assert.Equal(before, document.Text);
    }

    [Fact]
    public void TheGateAlone_AllowsAnIriNamedShape()
    {
        var (_, model) = Open("ex:S a sh:NodeShape .\n");

        var decision = ShaclEditGate.For(model, "res:http://example.org/S", truncated: false);

        Assert.True(decision.Applies);
        Assert.True(decision.Available);
        Assert.Equal("", decision.Reason);
        Assert.Equal("http://example.org/S", decision.ShapeIri);
    }

    // ---- the agreement between them ----------------------------------------------------------

    [Fact]
    public void BothLayers_SayTheIdenticalSentence()
    {
        var (document, model) = Open("[] a sh:NodeShape ; sh:property [ sh:path ex:p ] .\n");
        var blankId = ShaclProjection.Project(model).Cards.Single(card => card.Blank).Id;

        var fromTheGate = ShaclEditGate.For(model, blankId, truncated: false).Reason;
        var fromTheWriter = ShaclWriter.RemoveShapeWithSubtrees(document, model, "");

        // Not "both mention blank nodes" - the same string, so the two can never drift.
        Assert.Equal(fromTheGate, fromTheWriter);
        Assert.Equal(ShaclRefusals.BlankRooted, fromTheGate);
    }

    // ---- the gate's scoping, which keeps this reading's verbs off other readings' elements ----

    [Fact]
    public void TheGate_SaysNothingAboutATermThisFileDoesNotStateToBeAShape()
    {
        var (_, model) = Open("""
            ex:S a sh:NodeShape ; sh:targetClass ex:Person .
            ex:alice a ex:Person ; ex:name "Alice" .
            """ + "\n");

        // ex:alice is data, and ex:Person is targeted but not described as a shape: neither is
        // this reading's to edit, so no shapes verb is offered on either.
        Assert.False(ShaclEditGate.For(model, "res:http://example.org/alice", truncated: false).Applies);
        Assert.False(ShaclEditGate.For(model, "res:http://example.org/Person", truncated: false).Applies);
        Assert.True(ShaclEditGate.For(model, "res:http://example.org/S", truncated: false).Applies);
    }

    [Fact]
    public void TheGate_WithholdsUnderTruncation_WithTheFamilySentence()
    {
        var (_, model) = Open("ex:S a sh:NodeShape .\n");

        var decision = ShaclEditGate.For(model, "res:http://example.org/S", truncated: true);

        Assert.True(decision.Applies);
        Assert.False(decision.Available);
        Assert.Equal(RdfSelection.TruncatedRefusal, decision.Reason);
    }

    [Fact]
    public void TheGate_IgnoresIdsThatAreNotThisFamilys()
    {
        var (_, model) = Open("ex:S a sh:NodeShape .\n");

        Assert.False(ShaclEditGate.For(model, "", truncated: false).Applies);
        Assert.False(ShaclEditGate.For(model, null, truncated: false).Applies);
        Assert.False(ShaclEditGate.For(model, "shacl-edge:a|node|b", truncated: false).Applies);
    }
}
