using System.Text;
using EtAlii.Adp.Diagram.Rdf.Shacl;
using EtAlii.Adp.Documents;
using Xunit;

namespace EtAlii.Adp.Diagram.Rdf.Tests;

/// <summary>
/// The shapes projection: cards for node shapes, rows for property shapes, edges only between
/// shapes, the sh:class single-claimant rule, and the budget counting cards alone
/// (shacl-diagram Requirements 1 and 8).
/// </summary>
public class ShaclProjectionTests
{
    private const string Prelude = """
        @prefix sh: <http://www.w3.org/ns/shacl#> .
        @prefix ex: <http://example.org/> .
        @prefix xsd: <http://www.w3.org/2001/XMLSchema#> .

        """;

    private static ShaclProjectionResult Project(string body, int budget = ShaclProjection.DefaultBudget) =>
        ShaclProjection.Project(RdfParser.Parse(LineDocument.Parse(Prelude + body)), budget);

    [Fact]
    public void CardsAreShapes_RowsArePropertyShapes_AndPlumbingNeverDraws()
    {
        var projection = Project("""
            ex:PersonShape a sh:NodeShape ;
              sh:targetClass ex:Person ;
              sh:name "Person" ;
              sh:description "A person profile." ;
              sh:property [ sh:path ex:name ; sh:datatype xsd:string ; sh:minCount 1 ; sh:maxCount 1 ; sh:name "name" ] ;
              sh:property [ sh:path ex:age ; sh:datatype xsd:integer ] .
            ex:alice a ex:Person .
            """ + "\n");

        // One card: the shape. The co-resident instance never draws in this reading.
        var card = Assert.Single(projection.Cards);
        Assert.Equal("res:http://example.org/PersonShape", card.Id);
        Assert.Equal("ex:PersonShape", card.Display);
        Assert.Equal("Person", card.Name);
        Assert.Equal("A person profile.", card.Description);

        // Rows in source order, path first, constraints summarized, cardinality bracketed.
        Assert.Equal(2, card.Rows.Count);
        Assert.Equal(("ex:name", "name", "xsd:string", "[1..1]"), (card.Rows[0].Path, card.Rows[0].Name, card.Rows[0].Summary, card.Rows[0].Cardinality));
        Assert.Equal(("ex:age", "xsd:integer", "[0..*]"), (card.Rows[1].Path, card.Rows[1].Summary, card.Rows[1].Cardinality));
        Assert.All(card.Rows, row => Assert.True(row.Blank));

        // No edges: the target is a chip, the property shapes are rows.
        Assert.Empty(projection.Edges);
    }

    [Fact]
    public void FlagsAreWorn_DeactivatedSeverityClosed()
    {
        var projection = Project("""
            ex:S a sh:NodeShape ;
              sh:deactivated true ;
              sh:severity sh:Warning ;
              sh:closed true .
            """ + "\n");

        var card = Assert.Single(projection.Cards);
        Assert.True(card.Deactivated);
        Assert.True(card.Closed);
        Assert.Equal("sh:Warning", card.Severity);
    }

    [Fact]
    public void ShNode_DrawsAnEdge_FromCardAndFromRowAlike()
    {
        var projection = Project("""
            ex:A a sh:NodeShape ; sh:node ex:B .
            ex:B a sh:NodeShape .
            ex:C a sh:NodeShape ;
              sh:property [ sh:path ex:p ; sh:node ex:B ] .
            """ + "\n");

        Assert.Equal(3, projection.Cards.Count);
        Assert.Contains(projection.Edges, edge =>
            edge is { FromId: "res:http://example.org/A", ToId: "res:http://example.org/B", Kind: "node", Label: "node" });

        // The row-level reference edges from the containing card, labeled by the row's path.
        Assert.Contains(projection.Edges, edge =>
            edge is { FromId: "res:http://example.org/C", ToId: "res:http://example.org/B", Kind: "node", Label: "ex:p" });
    }

    [Fact]
    public void AnAnonymousNodeShapeReferencedByShNode_DrawsAsABlankCard()
    {
        var projection = Project("ex:A a sh:NodeShape ; sh:node [ sh:closed true ] .\n");

        Assert.Equal(2, projection.Cards.Count);
        var blank = Assert.Single(projection.Cards, card => card.Blank);
        Assert.True(blank.Closed);
        Assert.Contains(projection.Edges, edge => edge.FromId == "res:http://example.org/A" && edge.ToId == blank.Id);
    }

    [Fact]
    public void CombinatorOperands_EdgesForIriShapes_InlineRowWhenAnyOperandIsBlank()
    {
        var projection = Project("""
            ex:A a sh:NodeShape .
            ex:S sh:or ( ex:A [ sh:datatype xsd:date ] ) .
            ex:T sh:xone ( ex:A ) .
            """ + "\n");

        // The blank operand never becomes a card - it lives in the row.
        Assert.DoesNotContain(projection.Cards, card => card.Blank);

        Assert.Contains(projection.Edges, edge =>
            edge is { FromId: "res:http://example.org/S", ToId: "res:http://example.org/A", Kind: "or" });
        Assert.Contains(projection.Edges, edge =>
            edge is { FromId: "res:http://example.org/T", ToId: "res:http://example.org/A", Kind: "xone" });

        // The all-IRI combinator draws edges only; the mixed one also gets its completeness row.
        var sCard = projection.Cards.Single(card => card.Id == "res:http://example.org/S");
        var row = Assert.Single(sCard.Rows);
        Assert.Equal("or(ex:A, { datatype xsd:date })", row.Summary);
        var tCard = projection.Cards.Single(card => card.Id == "res:http://example.org/T");
        Assert.Empty(tCard.Rows);
    }

    [Fact]
    public void ShClass_EdgesOnlyForExactlyOneDrawnClaimant()
    {
        // Zero claimants: summary. One: edge. Two: summary again.
        var projection = Project("""
            ex:S a sh:NodeShape ;
              sh:property [ sh:path ex:knows ; sh:class ex:Person ] ;
              sh:property [ sh:path ex:owns ; sh:class ex:Thing ] ;
              sh:property [ sh:path ex:likes ; sh:class ex:Food ] .
            ex:PersonShape sh:targetClass ex:Person .
            ex:FoodShapeA sh:targetClass ex:Food .
            ex:FoodShapeB sh:targetClass ex:Food .
            """ + "\n");

        var sCard = projection.Cards.Single(card => card.Id == "res:http://example.org/S");

        // One drawn claimant for ex:Person - an edge, labeled by the path, nothing in the summary.
        var classEdge = Assert.Single(projection.Edges, edge => edge.Kind == "class");
        Assert.Equal(("res:http://example.org/S", "res:http://example.org/PersonShape", "ex:knows"), (classEdge.FromId, classEdge.ToId, classEdge.Label));
        Assert.Equal("", sCard.Rows.Single(row => row.Path == "ex:knows").Summary);

        // Zero claimants for ex:Thing, two for ex:Food - both stay in their rows.
        Assert.Equal("class ex:Thing", sCard.Rows.Single(row => row.Path == "ex:owns").Summary);
        Assert.Equal("class ex:Food", sCard.Rows.Single(row => row.Path == "ex:likes").Summary);
    }

    [Fact]
    public void AnOrphanIriPropertyShape_DrawsAsItsOwnCard_AReferencedOneRowsInstead()
    {
        var projection = Project("""
            ex:nameShape a sh:PropertyShape ; sh:path ex:name .
            ex:S a sh:NodeShape ; sh:property ex:refShape .
            ex:refShape a sh:PropertyShape ; sh:path ex:ref .
            """ + "\n");

        Assert.Contains(projection.Cards, card => card.Id == "res:http://example.org/nameShape");
        Assert.DoesNotContain(projection.Cards, card => card.Id == "res:http://example.org/refShape");
        var row = Assert.Single(projection.Cards.Single(card => card.Id == "res:http://example.org/S").Rows);
        Assert.Equal("ex:ref", row.Path);
        Assert.False(row.Blank); // an IRI-named property shape's row is editable ground
    }

    [Fact]
    public void PathSyntax_PrintsTheExpressionForms()
    {
        var projection = Project("""
            ex:S a sh:NodeShape ;
              sh:property [ sh:path [ sh:inversePath ex:parent ] ] ;
              sh:property [ sh:path ( ex:parent ex:member ) ] ;
              sh:property [ sh:path [ sh:alternativePath ( ex:a ex:b ) ] ] ;
              sh:property [ sh:path [ sh:zeroOrMorePath ex:next ] ] ;
              sh:property [ sh:path [ sh:oneOrMorePath ex:next ] ] ;
              sh:property [ sh:path [ sh:zeroOrOnePath ex:next ] ] .
            """ + "\n");

        Assert.Equal(
            ["^ex:parent", "ex:parent/ex:member", "ex:a|ex:b", "ex:next*", "ex:next+", "ex:next?"],
            projection.Cards.Single().Rows.Select(row => row.Path));
    }

    [Fact]
    public void SparqlConstraints_AreOpaqueRows()
    {
        var projection = Project("""
            ex:S a sh:NodeShape ;
              sh:sparql [ sh:select "SELECT $this WHERE { $this ex:p ex:o }" ] .
            """ + "\n");

        var row = Assert.Single(projection.Cards.Single().Rows);
        Assert.True(row.Sparql);
        Assert.Equal("SPARQL constraint", row.Summary);
    }

    [Fact]
    public void TheBudget_CountsCardsOnly_AndCutsDeterministically()
    {
        var builder = new StringBuilder();
        for (var i = 0; i < 12; i++)
        {
            builder.Append("ex:S").Append(i).Append(" a sh:NodeShape ; sh:property [ sh:path ex:p ] ; sh:targetClass ex:C").Append(i).Append(" .\n");
        }

        var projection = Project(builder.ToString(), budget: 5);

        Assert.Equal(5, projection.Shown);
        Assert.Equal(12, projection.Total);
        Assert.True(projection.Truncated);
        // First-N in discovery order; every drawn card keeps its rows and chips - they travel free.
        Assert.Equal(["res:http://example.org/S0", "res:http://example.org/S1", "res:http://example.org/S2", "res:http://example.org/S3", "res:http://example.org/S4"], projection.Cards.Select(card => card.Id));
        Assert.All(projection.Cards, card => Assert.Single(card.Rows));
        Assert.All(projection.Cards, card => Assert.Single(card.Targets));
    }

    [Fact]
    public void TheProjection_IsDeterministic()
    {
        const string body = """
            ex:S a sh:NodeShape ; sh:or ( ex:A [ sh:datatype xsd:date ] ) ;
              sh:property [ sh:path ex:p ; sh:class ex:Person ] .
            ex:A a sh:NodeShape ; sh:targetClass ex:Person .
            """ + "\n";

        // Structural comparison: the records hold lists, which record equality compares by
        // reference - the serialized forms compare by content.
        Assert.Equal(
            System.Text.Json.JsonSerializer.Serialize(Project(body)),
            System.Text.Json.JsonSerializer.Serialize(Project(body)));
    }
}
