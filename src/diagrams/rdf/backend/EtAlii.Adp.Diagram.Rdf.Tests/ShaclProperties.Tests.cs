using EtAlii.Adp.Documents;
using EtAlii.Adp.Documents.Wire;
using EtAlii.Adp.Context;
using EtAlii.Adp.Diagram.Rdf.Shacl;
using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.History;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.Rdf.Tests;

/// <summary>
/// The shapes reading's property grid (shacl-diagram Requirement 6.3), which is where
/// Requirement 4.1's promise is kept: blank-rooted constraint content is fully readable here even
/// though nothing about it is editable, and each read-only row says which path to take instead
/// rather than presenting a disabled box with no explanation (Requirement 6.4).
/// </summary>
public class ShaclPropertiesTests : IDisposable
{
    private readonly string _root;
    private readonly ServiceProvider _provider;
    private readonly string _body;

    private const string Shapes = """
        @prefix sh: <http://www.w3.org/ns/shacl#> .
        @prefix ex: <http://example.org/> .
        @prefix xsd: <http://www.w3.org/2001/XMLSchema#> .

        ex:PersonShape a sh:NodeShape ;
            sh:name "Person" ;
            sh:description "Every person we hold." ;
            sh:message "A person must have a name."@en ;
            sh:closed true ;
            sh:severity sh:Warning ;
            sh:targetClass ex:Person ;
            sh:property [ sh:path ex:name ; sh:datatype xsd:string ; sh:minCount 1 ; sh:maxCount 1 ] ;
            sh:sparql [ sh:select "SELECT $this WHERE { $this ex:name ?n }" ] .

        # A shape with no IRI of its own, stated free-standing rather than as some other shape's
        # property - so it is drawn as a card, and the identity boundary applies to a whole card.
        [] a sh:NodeShape ;
            sh:targetClass ex:Thing ;
            sh:name "Anonymous" .
        """;

    public ShaclPropertiesTests()
    {
        _root = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _body = IoPath.Combine(_root, "shapes.ttl");
        File.WriteAllText(_body, Shapes);

        _provider = new ServiceCollection()
            .AddSingleton<IReadOnlyList<DiagramDefinition>>(Diagram.Definitions)
            .AddCommands().AddHierarchyCommandHandlers()
            .AddRdf()
            .BuildServiceProvider();
    }

    public void Dispose()
    {
        _provider.Dispose();
        TestFolder.TryDelete(_root);
        GC.SuppressFinalize(this);
    }

    private RdfDocumentEntry Entry() => _provider.GetRequiredService<IRdfDocumentStore>().GetOrLoad(_body);

    private ContextTarget Target(string elementId, DiagramOrigin? origin) =>
        new(ContextScope.DiagramElement, _body, false, ShortGuid.NewShortGuid(), _root, default, elementId) { Origin = origin };

    private const string CardId = "res:http://example.org/PersonShape";

    private IReadOnlyList<ContextPropertyDefinition> Grid() =>
        ShaclProperties.Describe(Entry(), Target(CardId, ServiceCollectionAddShaclExtension.ShaclOrigin))!;

    private static ContextPropertyDefinition Row(IReadOnlyList<ContextPropertyDefinition> rows, string id) =>
        Assert.Single(rows, row => row.Id == id);

    [Fact]
    public void TheGrid_AnswersForItsOwnOriginOnly()
    {
        Assert.NotNull(ShaclProperties.Describe(Entry(), Target(CardId, ServiceCollectionAddShaclExtension.ShaclOrigin)));

        // Two readings over one .ttl produce byte-identical targets, so the element id cannot say
        // whose grid this is - only the origin can, exactly as it does for the action cases.
        Assert.Null(ShaclProperties.Describe(Entry(), Target(CardId, ServiceCollectionAddRdfExtension.RdfOrigin)));
        Assert.Null(ShaclProperties.Describe(Entry(), Target(CardId, ServiceCollectionAddSkosExtension.SkosOrigin)));
    }

    [Fact]
    public void ACard_ShowsItsIdentityAnnotationsAndFlags()
    {
        var rows = Grid();

        Assert.Equal("http://example.org/PersonShape", Row(rows, "shacl.iri").Value);
        Assert.Equal("Person", Row(rows, "shacl.name").Value);
        Assert.Equal("Every person we hold.", Row(rows, "shacl.description").Value);
        Assert.Equal("A person must have a name.", Row(rows, "shacl.message").Value);
        Assert.Equal("true", Row(rows, "shacl.closed").Value);
        Assert.Equal("sh:Warning", Row(rows, "shacl.severity").Value);
        Assert.Equal("false", Row(rows, "shacl.deactivated").Value);
    }

    [Fact]
    public void OnlyTheTwoAnnotationsRequirement5WritesAreEditable()
    {
        var rows = Grid();

        // Requirement 6.3 permits editing only where Requirement 5 does, and Requirement 5's
        // writers are gestures: targets, rows, activation, rename and removal all live on the
        // menu, where the dialogs and the removal count are.
        Assert.True(Row(rows, "shacl.name").IsEditable);
        Assert.True(Row(rows, "shacl.description").IsEditable);

        var readOnly = rows.Where(row => !row.IsEditable).ToArray();
        Assert.Equal(rows.Count - 2, readOnly.Length);

        // Requirement 6.4: never silently absent, and never silently disabled either.
        Assert.All(readOnly, row => Assert.NotEqual("", row.ReadOnlyReason));
    }

    [Fact]
    public void ABlankRootedRow_IsFullyReadableAndRefusedWithTheOneSentence()
    {
        var row = Row(Grid(), "shacl.row:0");

        // Requirement 4.1: the anonymous property shape has no identity, yet everything it says
        // is legible here - path, datatype, cardinality.
        Assert.Equal("ex:name", row.Label);
        Assert.Contains("xsd:string", row.Value, StringComparison.Ordinal);
        Assert.Contains("[1..1]", row.Value, StringComparison.Ordinal);

        // And the refusal is the shared sentence, not a paraphrase of it: a user who meets the
        // grid and the writer must read one refusal, not two that drifted apart.
        Assert.Equal(ShaclRefusals.BlankRooted, row.ReadOnlyReason);
    }

    [Fact]
    public void AnAbsentTarget_ReadsAsAFactRatherThanAFinding()
    {
        var row = Row(Grid(), "shacl.target:0");

        Assert.Equal("Target class", row.Label);

        // ex:Person is targeted but never described here - the normal case for a shapes graph
        // (Requirement 4.3), so it is stated plainly and never worded as a problem.
        Assert.Equal("ex:Person (not described in this file)", row.Value);
        Assert.DoesNotContain("error", row.Value, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("missing", row.Value, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ASparqlConstraint_ShowsItsQueryTextAndSaysItIsNotRun()
    {
        var row = Row(Grid(), "shacl.sparql:0");

        // Requirement 1.7: the text is readable, extracted and nothing more.
        Assert.Equal("SELECT $this WHERE { $this ex:name ?n }", row.Value);
        Assert.False(row.IsEditable);
        Assert.Contains("never executed", row.ReadOnlyReason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EditingTheName_LandsAsAMinimalDiffAndUndoesToTheByte()
    {
        var before = await File.ReadAllTextAsync(_body, TestContext.Current.CancellationToken);
        var command = ShaclProperties.CommandFor(
            Entry(), Target(CardId, ServiceCollectionAddShaclExtension.ShaclOrigin), "shacl.name", "Human");
        Assert.NotNull(command);

        var history = _provider.GetRequiredService<IHistoryStackStore>().Get(_root);
        Assert.True((await history.ExecuteAsync(command, TestContext.Current.CancellationToken)).IsSuccess);

        var after = await File.ReadAllTextAsync(_body, TestContext.Current.CancellationToken);
        Assert.Equal(before.Replace("\"Person\"", "\"Human\"", StringComparison.Ordinal), after);

        await history.UndoAsync(TestContext.Current.CancellationToken);
        Assert.Equal(before, await File.ReadAllTextAsync(_body, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void ABlankCard_IsReadableThroughoutAndWritableNowhere()
    {
        var blank = Assert.Single(ShaclProjection.Project(Entry().Model).Cards, card => card.Blank);
        var target = Target(blank.Id, ServiceCollectionAddShaclExtension.ShaclOrigin);
        var rows = ShaclProperties.Describe(Entry(), target)!;

        Assert.NotEmpty(rows);
        Assert.All(rows, row => Assert.False(row.IsEditable));
        Assert.Equal(ShaclRefusals.BlankRooted, Row(rows, "shacl.iri").ReadOnlyReason);

        // The gate refuses the write independently of the grid, so an edit dispatched directly
        // cannot land behind the read-only flags.
        Assert.Null(ShaclProperties.CommandFor(Entry(), target, "shacl.name", "anything"));
    }

    [Fact]
    public void ARowThatIsNotThisReadingsCard_GetsNoGridAtAll()
    {
        // A resource the file mentions but does not state to be a shape keeps the family's rows;
        // the shapes grid must decline rather than describe it as an empty shape.
        Assert.Null(ShaclProperties.Describe(
            Entry(),
            Target("res:http://example.org/Person", ServiceCollectionAddShaclExtension.ShaclOrigin)));
    }
}
