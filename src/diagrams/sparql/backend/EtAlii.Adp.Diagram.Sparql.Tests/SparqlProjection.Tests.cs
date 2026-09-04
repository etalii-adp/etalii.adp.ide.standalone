using System.Text;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.Sparql.Tests;

/// <summary>
/// The variable-as-node projection: one node per name through OPTIONAL, UNION and MINUS; the
/// shallowest-scope placement rule with its union lift; subquery collapse; concrete-term merging
/// by form; determinism; and the sanity cut.
/// </summary>
public class SparqlProjectionTests
{
    private static SparqlProjectionResult ProjectFixture(string name) =>
        SparqlProjection.Project(SparqlParser.Parse(
            File.ReadAllText(IoPath.Combine(AppContext.BaseDirectory, "Fixtures", name))));

    private static SparqlProjectionResult Project(string text) =>
        SparqlProjection.Project(SparqlParser.Parse(text));

    [Fact]
    public void OneVariable_OneNode_HoweverManyPatternsAndRegionsMentionIt()
    {
        // Arrange & act.
        var result = ProjectFixture("groups.rq");

        // Assert.
        // ?x is mentioned in the root, an OPTIONAL, all three UNION branches, a FILTER, a BIND
        // and a VALUES - and draws exactly once.
        var x = Assert.Single(result.Nodes, node => node.Id == "var:x");
        Assert.Equal(SparqlNodeKind.Variable, x.Kind);
        Assert.Equal("?x", x.Display);

        // Its degree on the canvas is its join count in the query: five pattern edges meet it.
        Assert.Equal(5, result.Edges.Count(edge => edge.FromId == "var:x" || edge.ToId == "var:x"));
        Assert.Equal(5, x.JoinCount);
        Assert.True(x.Projected);
    }

    [Fact]
    public void ThePlacementRule_ShallowestScope_WithRegionEdgesReachingOut()
    {
        // Arrange & act.
        var result = ProjectFixture("groups.rq");

        // Assert.
        // ?x is used inside and outside regions: it sits outside, on the open canvas.
        Assert.Equal("where", Assert.Single(result.Nodes, node => node.Id == "var:x").ScopePath);

        // ?y is used only inside the second OPTIONAL: it sits inside that region.
        Assert.Equal("where/optional.1", Assert.Single(result.Nodes, node => node.Id == "var:y").ScopePath);

        // The OPTIONAL's pattern edge reaches out from its region to the outside node - the
        // crossing is the join being shown.
        var optionalEdge = Assert.Single(result.Edges, edge => edge.ScopePath == "where/optional.0");
        Assert.Equal("var:x", optionalEdge.ToId);
    }

    [Fact]
    public void AUnionSharedName_LiftsToTheScopeThatJoinsTheBranches()
    {
        // Arrange & act.
        // ?u appears in both branches and nowhere else: its common ancestor is the union, and a
        // union holds branches rather than nodes, so it lifts to the union's parent.
        var result = Project(
            "PREFIX ex: <http://example.org/> SELECT ?u WHERE { { ?u ex:a 1 } UNION { ?u ex:b 2 } }");

        // Assert.
        Assert.Equal("where", Assert.Single(result.Nodes, node => node.Id == "var:u").ScopePath);

        // Each branch's edge still states its own scope - two crossings into the lifted node.
        Assert.Equal("where/union.0/branch.0", result.Edges[0].ScopePath);
        Assert.Equal("where/union.0/branch.1", result.Edges[1].ScopePath);
    }

    [Fact]
    public void AMinusRegion_FollowsTextualIdentity()
    {
        // Arrange & act.
        var result = ProjectFixture("groups.rq");

        // Assert.
        // ?s appears in the root and inside MINUS: one node, outside; the MINUS pattern's edge
        // carries its region's scope, and the region's label says what the region means.
        var s = Assert.Single(result.Nodes, node => node.Id == "var:s");
        Assert.Equal("where", s.ScopePath);
        Assert.Single(result.Edges, edge => edge.ScopePath == "where/minus.0" && edge.FromId == "var:s");
        Assert.Equal("MINUS", Assert.Single(result.Regions, region => region.Kind == "minus").Label);
    }

    [Fact]
    public void RegionsNestAsTheTreeNests_EachElementClaimedByExactlyOneRegion()
    {
        // Arrange & act.
        var result = Project(
            "PREFIX ex: <http://example.org/> SELECT * WHERE { OPTIONAL { ?a ex:p ?b . OPTIONAL { ?b ex:q ?c } } }");

        // Assert.
        var outer = Assert.Single(result.Regions, region => region.ScopePath == "where/optional.0");
        var inner = Assert.Single(result.Regions, region => region.ScopePath == "where/optional.0/optional.0");
        Assert.Equal("", outer.ParentRegionId);
        Assert.Equal(outer.Id, inner.ParentRegionId);

        // ?c lives in the inner region alone; ?b sits in the outer, referenced by both.
        Assert.Equal(inner.ScopePath, Assert.Single(result.Nodes, node => node.Id == "var:c").ScopePath);
        Assert.Equal(outer.ScopePath, Assert.Single(result.Nodes, node => node.Id == "var:b").ScopePath);
    }

    [Fact]
    public void ASubquery_CollapsesToOneNode_WithAJoinEdgePerProjectedName()
    {
        // Arrange & act.
        var result = ProjectFixture("groups.rq");

        // Assert.
        var collapsed = Assert.Single(result.Nodes, node => node.Kind == SparqlNodeKind.SubSelect);
        Assert.Equal("SELECT ?s (MAX(?v) AS ?best)", collapsed.Display);
        Assert.Equal("sub:where.0", collapsed.Id);

        // Its internal variable ?v is not drawn; its projected names join outward as edges.
        Assert.DoesNotContain(result.Nodes, node => node.Id == "var:v");
        Assert.Single(result.Edges, edge => edge.FromId == collapsed.Id && edge.ToId == "var:s");
        Assert.Single(result.Edges, edge => edge.FromId == collapsed.Id && edge.ToId == "var:best");
    }

    [Fact]
    public void ConcreteTerms_MergeByForm_WhichAssertsSameTermAndNeverAJoin()
    {
        // Arrange & act.
        var result = Project(
            "PREFIX ex: <http://example.org/> SELECT * WHERE { ?a ex:knows ex:carol . ?b ex:knows ex:carol . ?c ex:age 42 . ?d ex:age 42 }");

        // Assert.
        // One node per distinct IRI and per distinct literal form, with the edges fanning in.
        Assert.Single(result.Nodes, node => node.Id == "iri:http://example.org/carol");
        Assert.Equal(2, result.Edges.Count(edge => edge.ToId == "iri:http://example.org/carol"));
        var literal = Assert.Single(result.Nodes, node => node.Kind == SparqlNodeKind.Literal);
        Assert.Equal("42", literal.Display);
        Assert.Equal(2, result.Edges.Count(edge => edge.ToId == literal.Id));
    }

    [Fact]
    public void APropertyPathEdge_CarriesItsLabelAsWrittenAndSaysItIsAPath()
    {
        // Arrange & act.
        var result = ProjectFixture("constructs.rq");

        // Assert.
        var path = Assert.Single(result.Edges, edge => edge.IsPath);
        Assert.Equal("foaf:knows+", path.Label);
    }

    [Fact]
    public void AnnotationsAnchorToWhatTheyConstrainDefineOrFeed()
    {
        // Arrange & act.
        var result = ProjectFixture("groups.rq");

        // Assert.
        // The OPTIONAL's filter anchors to its region; the root's filter floats on the canvas.
        var regionFilter = Assert.Single(result.Annotations, a => a.ScopePath == "where/optional.0");
        Assert.Equal("region:where/optional.0", regionFilter.AttachedToId);
        var rootFilter = Assert.Single(result.Annotations, a => a.Kind == "filter" && a.ScopePath == "where");
        Assert.Equal("", rootFilter.AttachedToId);

        // BIND anchors to the variable it defines; VALUES to the first variable it feeds.
        Assert.Equal("var:doubled", Assert.Single(result.Annotations, a => a.Kind == "bind").AttachedToId);
        Assert.Equal("var:x", Assert.Single(result.Annotations, a => a.Kind == "values").AttachedToId);
    }

    [Fact]
    public void TheHeaderCarriesTheFormAndTheFrameRows()
    {
        // Arrange & act.
        var result = ProjectFixture("constructs.rq");

        // Assert.
        Assert.Equal("SELECT DISTINCT", result.HeaderForm);
        Assert.Equal(
            ["FROM <http://example.org/graphs/people>", "GROUP BY ?person ?name", "HAVING (COUNT(?friend) > 1)", "ORDER BY DESC(?friends) ?name", "LIMIT 10", "OFFSET 5"],
            result.HeaderRows);
    }

    [Fact]
    public void ATemplate_DrawsAsItsOwnRegion_SharingTheVariableNodes()
    {
        // Arrange & act.
        var result = ProjectFixture("construct-form.rq");

        // Assert.
        var template = Assert.Single(result.Regions, region => region.Kind == "template");
        Assert.Equal("CONSTRUCT", template.Label);

        // ?s and ?o are matched in the where clause and rebuilt in the template: one node each,
        // on the open canvas, with the template's edge reaching in.
        Assert.Single(result.Nodes, node => node.Id == "var:s");
        Assert.Single(result.Edges, edge => edge.ScopePath == "template" && edge.Label == "ex:mirrored");
    }

    [Fact]
    public void IdsAreStableAcrossReparsesOfAnUnchangedFile()
    {
        // Arrange.
        var text = File.ReadAllText(IoPath.Combine(AppContext.BaseDirectory, "Fixtures", "groups.rq"));

        // Act.
        var first = SparqlProjection.Project(SparqlParser.Parse(text));
        var second = SparqlProjection.Project(SparqlParser.Parse(text));

        // Assert: same elements, same ids, same order - determinism is the whole point.
        Assert.Equal(first.Nodes.Select(n => n.Id), second.Nodes.Select(n => n.Id));
        Assert.Equal(first.Edges.Select(e => e.Id), second.Edges.Select(e => e.Id));
        Assert.Equal(first.Regions.Select(r => r.Id), second.Regions.Select(r => r.Id));
        Assert.Equal(first.Annotations.Select(a => a.Id), second.Annotations.Select(a => a.Id));
    }

    [Fact]
    public void ABareDescribe_DrawsItsTarget()
    {
        // Arrange & act: a DESCRIBE with no where clause has nothing but its target, so if the
        // target is not drawn the diagram is empty. Found by a vendored UniProt example, which
        // is exactly what vendoring real queries is for.
        var result = Project("DESCRIBE <http://purl.uniprot.org/embl-cds/AAO89367.1>");

        // Assert.
        var node = Assert.Single(result.Nodes);
        Assert.Equal(SparqlNodeKind.Iri, node.Kind);
        Assert.Equal("http://purl.uniprot.org/embl-cds/AAO89367.1", node.Full);
        Assert.Equal("DESCRIBE <http://purl.uniprot.org/embl-cds/AAO89367.1>", result.HeaderForm);
    }

    [Fact]
    public void ADescribesPrefixedTarget_ResolvesToTheSameNodeItsPatternsUse()
    {
        // Arrange & act: the target and the where clause name one thing, so they share a node.
        var result = Project("PREFIX ex: <http://example.org/> DESCRIBE ex:thing WHERE { ?s ex:near ex:thing }");

        // Assert.
        Assert.Single(result.Nodes, node => node.Id == "iri:http://example.org/thing");
    }

    [Fact]
    public void AnonymousVariables_GetDocumentOrderOrdinals()
    {
        // Arrange & act.
        var result = Project(
            "PREFIX ex: <http://example.org/> SELECT * WHERE { [ ex:p ?a ] ex:q [ ex:r ?b ] }");

        // Assert.
        var anonymous = result.Nodes.Where(node => node.Kind == SparqlNodeKind.Anonymous).ToList();
        Assert.Equal(2, anonymous.Count);
        Assert.Equal(["anon:0", "anon:1"], anonymous.Select(node => node.Id));
    }

    [Fact]
    public void ADegeneratelyLargeQuery_IsCutHonestly()
    {
        // Arrange: a generated monster - hand-written queries never meet the bound.
        var text = new StringBuilder("PREFIX ex: <http://example.org/>\nSELECT * WHERE {\n");
        for (var i = 0; i < 400; i++)
        {
            text.Append($"  ?s{i} ex:p{i} ?o{i} .\n");
        }

        text.Append("}\n");

        // Act.
        var result = Project(text.ToString());

        // Assert.
        Assert.True(result.Truncated);
        Assert.Equal(SparqlProjection.SanityBound, result.Shown);
        Assert.Equal(1200, result.Total); // 800 variable nodes + 400 edges.
        // Every kept edge still has both endpoints - an honest first-N, never a dangling line.
        var keptIds = result.Nodes.Select(node => node.Id).ToHashSet();
        Assert.All(result.Edges, edge =>
        {
            Assert.Contains(edge.FromId, keptIds);
            Assert.Contains(edge.ToId, keptIds);
        });
    }
}
