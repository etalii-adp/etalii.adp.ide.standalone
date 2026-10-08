using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.Sparql.Tests;

/// <summary>
/// The parser over its three tiers: the structural constructs of Requirement 1.1, expression and
/// path text kept exactly as written (never as trees), and the by-name refusal of SPARQL Update.
/// </summary>
public class SparqlParserTests
{
    private static SparqlQueryModel ParseFixture(string name) =>
        SparqlParser.Parse(File.ReadAllText(IoPath.Combine(AppContext.BaseDirectory, "Fixtures", name)));

    // -- Tier one: parsed structurally ----------------------------------------------------------

    [Fact]
    public void TheConstructCorpus_ParsesWithEveryStructuralConstructAccountedFor()
    {
        // Arrange & act.
        var model = ParseFixture("constructs.rq");

        // Assert.
        // The prologue, in order, and the base.
        Assert.Equal(["foaf", "ex"], model.Prefixes.Select(p => p.Prefix));
        Assert.Equal(["http://xmlns.com/foaf/0.1/", "http://example.org/"], model.Prefixes.Select(p => p.Iri));
        Assert.Equal("http://example.org/base/", model.BaseIri);

        // The form and its frame.
        Assert.Equal(SparqlQueryForm.Select, model.Form);
        Assert.True(model.Distinct);
        Assert.Equal(["FROM <http://example.org/graphs/people>"], model.DatasetClauses);

        // 'a' became rdf:type without losing its spelling.
        var type = model.Where.Patterns.First(p => p.Predicate is IriTerm { Iri: SparqlVocabulary.Type });
        Assert.Equal("a", type.Predicate.AsWritten);
        Assert.Equal("http://xmlns.com/foaf/0.1/Person", Assert.IsType<IriTerm>(type.Object).Iri);

        // The predicate list and object list fanned out from one subject.
        var person = new VariableTerm("person", "?person");
        var knows = model.Where.Patterns.Where(p => p.Subject.Equals(person) && p.Predicate is IriTerm { Iri: "http://xmlns.com/foaf/0.1/knows" }).ToList();
        Assert.Equal(2, knows.Count);
        Assert.Contains(knows, p => p.Object is IriTerm { Iri: "http://example.org/carol" });

        // Bare numbers, booleans and typed/tagged strings carry their datatypes.
        Assert.Contains(model.Where.Patterns, p => p.Object is LiteralTerm { Lexical: "42", DatatypeIri: SparqlVocabulary.XsdInteger });
        Assert.Contains(model.Where.Patterns, p => p.Object is LiteralTerm { Lexical: "3.14", DatatypeIri: SparqlVocabulary.XsdDecimal });
        Assert.Contains(model.Where.Patterns, p => p.Object is LiteralTerm { Lexical: "true", DatatypeIri: SparqlVocabulary.XsdBoolean });
        Assert.Contains(model.Where.Patterns, p => p.Object is LiteralTerm { Lexical: "hello\nworld", Language: "en" });
        Assert.Contains(model.Where.Patterns, p => p.Object is LiteralTerm { Lexical: "80", DatatypeIri: "http://www.w3.org/2001/XMLSchema#int" });

        // The relative IRI resolved against the base.
        Assert.Contains(model.Where.Patterns, p => p.Object is IriTerm { Iri: "http://example.org/base/relative/place" });

        // The labeled blank node is one anonymous term across its uses.
        var labeled = model.Where.Patterns.Where(p => p.Subject is AnonymousTerm { Label: "_:g" }).ToList();
        Assert.Equal(2, labeled.Count);
        Assert.Same(labeled[0].Subject, labeled[1].Subject);

        // The anonymous property list stated its triple and became a subject itself.
        Assert.Contains(model.Where.Patterns, p => p is { Subject: AnonymousTerm { Label: "" }, Predicate: IriTerm { Iri: "http://example.org/leader" } });
        Assert.Contains(model.Where.Patterns, p => p is { Subject: AnonymousTerm { Label: "" }, Predicate: IriTerm { Iri: "http://example.org/size" } });

        // The collection expanded to its cons cells, ending at nil.
        Assert.Contains(model.Where.Patterns, p => p.Predicate is IriTerm { Iri: SparqlVocabulary.First });
        Assert.Contains(model.Where.Patterns, p => p is { Predicate: IriTerm { Iri: SparqlVocabulary.Rest }, Object: IriTerm { Iri: SparqlVocabulary.Nil } });

        // The solution modifiers arrived as written rows, in order - frame, never structure.
        Assert.Equal(
            ["GROUP BY ?person ?name", "HAVING (COUNT(?friend) > 1)", "ORDER BY DESC(?friends) ?name", "LIMIT 10", "OFFSET 5"],
            model.ModifierRows);
    }

    [Fact]
    public void TheGroupCorpus_BuildsTheScopeTreeWithStableOrdinalsAndPaths()
    {
        // Arrange & act.
        var model = ParseFixture("groups.rq");
        var root = model.Where;

        // Assert.
        Assert.Equal("where", root.Path);

        var optionals = root.Children.Where(c => c.Kind == GroupScopeKind.Optional).ToList();
        Assert.Equal(2, optionals.Count);
        Assert.Equal("where/optional.0", optionals[0].Path);
        Assert.Equal("where/optional.1", optionals[1].Path);

        var union = Assert.Single(root.Children, c => c.Kind == GroupScopeKind.Union);
        Assert.Equal(3, union.Children.Count);
        Assert.All(union.Children, branch => Assert.Equal(GroupScopeKind.Branch, branch.Kind));
        Assert.Equal("where/union.0/branch.2", union.Children[2].Path);

        Assert.Single(root.Children, c => c.Kind == GroupScopeKind.Minus);

        var graph = Assert.Single(root.Children, c => c.Kind == GroupScopeKind.Graph);
        Assert.Equal("?g", graph.Label);

        var service = Assert.Single(root.Children, c => c.Kind == GroupScopeKind.Service);
        Assert.Equal("SILENT <http://example.org/sparql>", service.Label);
    }

    [Fact]
    public void TheFourForms_AllParse()
    {
        // Arrange & act & assert.
        Assert.Equal(SparqlQueryForm.Ask, ParseFixture("ask-form.rq").Form);

        var describe = ParseFixture("describe-form.rq");
        Assert.Equal(SparqlQueryForm.Describe, describe.Form);
        Assert.Equal(["?s", "ex:thing"], describe.DescribeTargets);
        Assert.Equal(["LIMIT 5"], describe.ModifierRows);

        var construct = ParseFixture("construct-form.rq");
        Assert.Equal(SparqlQueryForm.Construct, construct.Form);
        Assert.NotNull(construct.Template);
        Assert.Equal(GroupScopeKind.Template, construct.Template.Kind);
        var templated = Assert.Single(construct.Template.Patterns);
        Assert.Equal("http://example.org/mirrored", Assert.IsType<IriTerm>(templated.Predicate).Iri);

        // The short form: the template is the pattern itself.
        var shortForm = ParseFixture("construct-where-short.rq");
        Assert.NotNull(shortForm.Template);
        Assert.Equal(shortForm.Where.Patterns.Count, shortForm.Template.Patterns.Count);
    }

    // -- Tier two: recognized but kept as written --------------------------------------------------

    [Fact]
    public void APropertyPath_StaysAnEdgeLabelAsWritten_NeverAParsedStructure()
    {
        // Arrange & act.
        var model = ParseFixture("constructs.rq");

        // Assert.
        var path = Assert.Single(model.Where.Patterns, p => p.Predicate is PathTerm);
        Assert.Equal("foaf:knows+", path.Predicate.AsWritten);
    }

    [Fact]
    public void ExpressionsArriveAsSourceTextWithTheirVariableReferences_NeverAsTrees()
    {
        // Arrange & act.
        var model = ParseFixture("groups.rq");
        var root = model.Where;

        // Assert.
        // The FILTER inside the first OPTIONAL, exactly as its author spaced it.
        var optionalFilter = Assert.Single(root.Children.First(c => c.Kind == GroupScopeKind.Optional).Constraints);
        Assert.Equal(SparqlConstraintKind.Filter, optionalFilter.Kind);
        Assert.Equal("FILTER(?x > 3)", optionalFilter.Text);
        Assert.Equal(["x"], optionalFilter.ReferencedVariables);

        // FILTER NOT EXISTS keeps its whole group as text - its patterns are a constraint, not structure.
        var notExists = Assert.Single(root.Constraints, c => c.Kind == SparqlConstraintKind.Filter);
        Assert.Equal("FILTER NOT EXISTS { ?s ex:hidden true }", notExists.Text);

        // BIND names what it defines and slices what defines it.
        var bind = Assert.Single(root.Constraints, c => c.Kind == SparqlConstraintKind.Bind);
        Assert.Equal("BIND(?x * 2 AS ?doubled)", bind.Text);
        Assert.Equal("doubled", bind.DefinedVariable);
        Assert.Equal(["x"], bind.ReferencedVariables);

        // VALUES is one annotation feeding its variables.
        var values = Assert.Single(root.Constraints, c => c.Kind == SparqlConstraintKind.Values);
        Assert.Equal("VALUES ?x { 1 2 3 }", values.Text);
        Assert.Equal(["x"], values.ReferencedVariables);
    }

    [Fact]
    public void ASubquery_CollapsesToItsProjection()
    {
        // Arrange & act.
        var model = ParseFixture("groups.rq");

        // Assert.
        var subSelect = Assert.Single(model.Where.SubSelects);
        Assert.Equal("SELECT ?s (MAX(?v) AS ?best)", subSelect.ProjectionText);
        Assert.Equal(["s", "best"], subSelect.ProjectedNames);
        Assert.False(subSelect.ProjectsAll);
        Assert.StartsWith("{", subSelect.Text);
        Assert.EndsWith("}", subSelect.Text);
    }

    // -- The variable index: joins are countable before anything is drawn --------------------------

    [Fact]
    public void TheVariableIndex_CountsJoinsAndTracksScopesAndProjection()
    {
        // Arrange & act.
        var model = ParseFixture("groups.rq");

        // Assert.
        var x = model.Variables["x"];
        // ?x appears in five triple patterns: the root, one OPTIONAL, and all three UNION branches.
        Assert.Equal(5, x.PatternOccurrences);
        Assert.True(x.Projected);
        // Its scope list starts at the shallowest scope that mentions it - where its node will live.
        Assert.Equal("where", x.ScopePaths[0]);
        Assert.Contains("where/optional.0", x.ScopePaths);
        Assert.Contains("where/union.0/branch.0", x.ScopePaths);
        Assert.Contains("where/union.0/branch.1", x.ScopePaths);
        Assert.Contains("where/union.0/branch.2", x.ScopePaths);

        // ?y lives only inside the second OPTIONAL, and is not projected.
        var y = model.Variables["y"];
        Assert.False(y.Projected);
        Assert.Equal(["where/optional.1"], y.ScopePaths);

        // ?doubled is defined by its BIND, and the definition travels with it.
        var doubled = model.Variables["doubled"];
        Assert.Equal("BIND(?x * 2 AS ?doubled)", doubled.DefiningExpression);

        // The subquery's projected names join the outer scope through the collapsed node.
        Assert.Contains("where", model.Variables["best"].ScopePaths);
    }

    [Fact]
    public void SelectStar_ResolvesToTheInScopeNames()
    {
        // Arrange & act.
        var model = SparqlParser.Parse("PREFIX ex: <http://example.org/> SELECT * WHERE { ?a ex:p ?b . OPTIONAL { ?a ex:q ?c } }");

        // Assert.
        Assert.True(model.ProjectsAll);
        Assert.Equal(["a", "b", "c"], model.Projection.Select(item => item.Name));
        Assert.True(model.Variables["c"].Projected);
    }

    // -- Line endings: the parser is indifferent, which is what the byte-kept pair proves -----------

    [Theory]
    [InlineData("crlf-line-endings.rq")]
    [InlineData("lf-line-endings.rq")]
    [InlineData("no-trailing-newline.rq")]
    public void LineEndingVariants_ParseToTheSameQuery(string name)
    {
        // Arrange & act.
        var model = ParseFixture(name);

        // Assert.
        Assert.Equal(SparqlQueryForm.Select, model.Form);
        var pattern = Assert.Single(model.Where.Patterns);
        Assert.Equal("http://example.org/p", Assert.IsType<IriTerm>(pattern.Predicate).Iri);
        Assert.Equal(["s"], model.Projection.Select(item => item.Name));
    }

    // -- Tier three: refused by name -----------------------------------------------------------------

    [Fact]
    public void ASparqlUpdateDocument_IsRefusedNamingSparqlUpdate()
    {
        // Arrange.
        var text = File.ReadAllText(IoPath.Combine(AppContext.BaseDirectory, "Fixtures", "update-document.rq"));

        // Act.
        var exception = Assert.Throws<SparqlParseException>(() => SparqlParser.Parse(text));

        // Assert.
        Assert.Contains("SPARQL Update", exception.Message);
        Assert.Contains("INSERT", exception.Message);
        Assert.Equal(2, exception.Line);
    }

    [Fact]
    public void AFileThatDoesNotParse_NamesItsLineAndReason()
    {
        // Arrange.
        var text = File.ReadAllText(IoPath.Combine(AppContext.BaseDirectory, "Fixtures", "broken.rq"));

        // Act.
        var exception = Assert.Throws<SparqlParseException>(() => SparqlParser.Parse(text));

        // Assert.
        Assert.Equal(4, exception.Line);
        Assert.Contains("undeclared", exception.Message);
    }

    [Fact]
    public void AnUnclosedGroup_NamesTheOpeningLine()
    {
        // Arrange & act.
        var exception = Assert.Throws<SparqlParseException>(() => SparqlParser.Parse("SELECT * WHERE {\n  ?s ?p ?o .\n"));

        // Assert.
        Assert.Contains("never closed", exception.Message);
    }
}
