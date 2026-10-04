using EtAlii.Adp.Diagram.Rdf.Shacl;
using EtAlii.Adp.Documents;
using Xunit;

namespace EtAlii.Adp.Diagram.Rdf.Tests;

/// <summary>
/// The structural guarantee of shacl-diagram Requirements 1.3 and 4.3, tested as the claim it
/// is: a target is a chip on its shape's card - never an edge, never a fabricated element - and
/// every element the projection emits is a discovered shape, so nothing can dangle and nothing
/// can be invented, whatever the targets say. Absence is a fact the chip states, not a finding.
/// </summary>
public class ShaclTargetChipsTests
{
    private const string Prelude = """
        @prefix sh: <http://www.w3.org/ns/shacl#> .
        @prefix ex: <http://example.org/> .
        @prefix rdfs: <http://www.w3.org/2000/01/rdf-schema#> .

        """;

    private static ShaclProjectionResult Project(string body) =>
        ShaclProjection.Project(RdfParser.Parse(LineDocument.Parse(Prelude + body)));

    /// <summary>
    /// The invariant a placeholder node or dangling edge would break: the projection's whole
    /// element universe - card ids and both endpoints of every edge - is exactly the discovered
    /// shape set's ids. A target term that is not a shape cannot appear anywhere in it.
    /// </summary>
    private static void AssertElementUniverseIsShapesOnly(string body)
    {
        var model = RdfParser.Parse(LineDocument.Parse(Prelude + body));
        var shapeIds = ShaclShapeDiscovery.Discover(model).Select(shape => ShaclProjection.IdOf(shape.Key)).ToHashSet(StringComparer.Ordinal);
        var projection = ShaclProjection.Project(model);

        Assert.All(projection.Cards, card => Assert.Contains(card.Id, shapeIds));
        Assert.All(projection.Edges, edge =>
        {
            Assert.Contains(edge.FromId, shapeIds);
            Assert.Contains(edge.ToId, shapeIds);
        });
    }

    [Theory]
    [InlineData("ex:S sh:targetClass ex:Person .\n")]
    [InlineData("ex:S sh:targetNode ex:alice .\n")]
    [InlineData("ex:S sh:targetSubjectsOf ex:knows .\n")]
    [InlineData("ex:S sh:targetObjectsOf ex:knows .\n")]
    public void AnAbsentTargetTerm_YieldsAChipAndNothingElse(string body)
    {
        var projection = Project(body);

        // One card - the shape. The targeted term, absent from the file, is no element at all.
        var card = Assert.Single(projection.Cards);
        Assert.Empty(projection.Edges);

        var chip = Assert.Single(card.Targets);
        Assert.False(chip.DescribedInFile);
        Assert.NotEqual("", chip.TermIri);
        Assert.NotEqual("", chip.TermDisplay);

        // Fully addressable: shape IRI + target predicate + term IRI, no canvas selection needed.
        Assert.Equal("http://example.org/S", chip.ShapeIri);
        Assert.StartsWith("http://www.w3.org/ns/shacl#target", chip.PredicateIri, StringComparison.Ordinal);

        AssertElementUniverseIsShapesOnly(body);
    }

    [Fact]
    public void APresentTargetTerm_StillYieldsOnlyAChip_TheFlagIsTheOnlyDifference()
    {
        const string body = """
            ex:S sh:targetClass ex:Person .
            ex:Person a rdfs:Class .
            ex:alice a ex:Person .
            """ + "\n";
        var projection = Project(body);

        // The class and the instance are subjects in this file - and still not this reading's
        // elements. Present and absent targets differ in exactly one bit.
        var card = Assert.Single(projection.Cards);
        Assert.Empty(projection.Edges);
        Assert.True(Assert.Single(card.Targets).DescribedInFile);

        AssertElementUniverseIsShapesOnly(body);
    }

    [Fact]
    public void ATargetNamingAnotherDrawnShape_StillDrawsNoEdge()
    {
        // The trap case: the targeted term IS a drawn card. A naive projection would connect
        // them; the chip stays a chip and the edge count stays zero.
        const string body = """
            ex:A sh:targetClass ex:B .
            ex:B a sh:NodeShape .
            """ + "\n";
        var projection = Project(body);

        Assert.Equal(2, projection.Cards.Count);
        Assert.Empty(projection.Edges);

        var chip = Assert.Single(projection.Cards.Single(card => card.Id == "res:http://example.org/A").Targets);
        Assert.True(chip.DescribedInFile);
        Assert.Equal("http://example.org/B", chip.TermIri);

        AssertElementUniverseIsShapesOnly(body);
    }

    [Fact]
    public void AMixedTargetMatrix_EveryChipCompleteAndAddressable()
    {
        const string body = """
            ex:S a sh:NodeShape ;
              sh:targetClass ex:Present, ex:Absent ;
              sh:targetNode "a literal node" ;
              sh:targetSubjectsOf ex:knows .
            ex:Present a rdfs:Class .
            """ + "\n";
        var projection = Project(body);

        var card = Assert.Single(projection.Cards);
        Assert.Empty(projection.Edges);
        Assert.Equal(4, card.Targets.Count);

        Assert.Contains(card.Targets, chip => chip is { Kind: ShaclTargetKind.Class, TermIri: "http://example.org/Present", DescribedInFile: true });
        Assert.Contains(card.Targets, chip => chip is { Kind: ShaclTargetKind.Class, TermIri: "http://example.org/Absent", DescribedInFile: false });

        // A literal target: the lexical form is the display, the IRI slot honestly empty.
        Assert.Contains(card.Targets, chip => chip is { Kind: ShaclTargetKind.Node, TermDisplay: "a literal node", TermIri: "", DescribedInFile: false });
        Assert.Contains(card.Targets, chip => chip is { Kind: ShaclTargetKind.SubjectsOf, TermIri: "http://example.org/knows" });

        // Every explicit chip is fully addressable for removal.
        Assert.All(card.Targets, AssertChip);

        AssertElementUniverseIsShapesOnly(body);
        return;

        void AssertChip(ShaclTargetChip chip)
        {
            ArgumentNullException.ThrowIfNull(chip);

            Assert.Equal("http://example.org/S", chip.ShapeIri);
            Assert.NotEqual("", chip.PredicateIri);
        }
    }

    [Fact]
    public void TheImplicitClassTarget_IsAChipWithNoAddress_NothingToRemove()
    {
        var projection = Project("ex:Person a rdfs:Class, sh:NodeShape .\n");

        var card = Assert.Single(projection.Cards);
        var chip = Assert.Single(card.Targets);
        Assert.Equal(ShaclTargetKind.ImplicitClass, chip.Kind);
        Assert.True(chip.DescribedInFile);
        Assert.Equal("http://example.org/Person", chip.TermIri);

        // No triple states this target, so no menu can remove it: the address's predicate slot
        // is deliberately empty.
        Assert.Equal("", chip.PredicateIri);
    }

    [Fact]
    public void AChipOnAnAnonymousShape_HasNoShapeAddress_TheBoundaryOwnsIt()
    {
        var projection = Project("[] sh:targetClass ex:Person ; sh:closed true .\n");

        var card = Assert.Single(projection.Cards);
        Assert.True(card.Blank);
        var chip = Assert.Single(card.Targets);

        // The chip renders like any other; its shape slot is empty, which is what the provider's
        // blank-rooted refusal keys off - readable everywhere, removable nowhere.
        Assert.Equal("", chip.ShapeIri);
        Assert.Equal("http://www.w3.org/ns/shacl#targetClass", chip.PredicateIri);
    }
}
