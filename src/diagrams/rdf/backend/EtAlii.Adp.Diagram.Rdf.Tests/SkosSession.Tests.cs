using EtAlii.Adp.Backend;
using EtAlii.Adp.Backend.Diagrams;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.Rdf.Tests;

/// <summary>
/// The scheme reading over the family store (skos-diagram Requirement 2): the definition's
/// shared-extension stance, the factory seeding the chooser from the registration, one store
/// serving both readings of one file, and the session's refusals naming their reasons.
/// </summary>
public class SkosSessionTests : IDisposable
{
    private readonly string _root;
    private readonly ServiceProvider _provider;

    public SkosSessionTests()
    {
        _root = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _provider = new ServiceCollection()
            .AddSingleton<IReadOnlyList<DiagramDefinition>>(Diagram.Definitions)
            .AddCommands()
            .AddRdf()
            .BuildServiceProvider();
    }

    public void Dispose()
    {
        _provider.Dispose();
        TestFolder.TryDelete(_root);
    }

    private const string Vocabulary = """
        @prefix skos: <http://www.w3.org/2004/02/skos/core#> .
        @prefix ex: <http://example.org/> .

        ex:scheme a skos:ConceptScheme ; skos:prefLabel "Drinks"@en ; skos:hasTopConcept ex:tea .
        ex:tea a skos:Concept ; skos:topConceptOf ex:scheme ;
            skos:prefLabel "Tea"@en ; skos:prefLabel "Thee"@nl ; skos:notation "T1" .
        """;

    private string WriteBody(string name = "drinks.ttl", string text = Vocabulary)
    {
        var path = IoPath.Combine(_root, name);
        File.WriteAllText(path, text);
        return path;
    }

    private IDiagramSession Open(string bodyPath, string? registrationPath)
    {
        var factory = _provider.GetServices<IDiagramSessionFactory>()
            .Single(candidate => candidate.Origin == ServiceCollectionAddSkosExtension.SkosOrigin);
        return factory.Open(ShortGuid.NewShortGuid(), _root, bodyPath, registrationPath);
    }

    [Fact]
    public void TheDefinition_NeverClaimsABareBody_AndDeclaresTheFamilyExtensions()
    {
        // Assert: the routing arrangement - SharedExtension keeps a bare .ttl the anchor's,
        // while Add offers this reading for both serializations.
        Assert.Contains(Diagram.Skos, Diagram.Definitions);
        Assert.True(Diagram.Skos.SharedExtension);
        Assert.True(Diagram.Skos.DeclaresExtension(".ttl"));
        Assert.True(Diagram.Skos.DeclaresExtension(".nt"));
        Assert.False(Diagram.Rdf.SharedExtension);
        Assert.Equal(new DiagramOrigin("w3c", "skos"), Diagram.Skos.Origin);
    }

    [Fact]
    public void TheSession_RendersTheVocabulary_InTheRegistrationsLanguage()
    {
        // Arrange: a registration whose language: header sits in the band.
        var body = WriteBody();
        var registration = IoPath.Combine(_root, "drinks.adp");
        File.WriteAllText(registration, "w3c/skos\r\nbody: drinks.ttl\r\nlanguage: nl\r\n");

        // Act.
        var session = (SkosSession)Open(body, registration);
        var baseline = session.Baseline();

        // Assert: the chooser was seeded with nl - the Dutch label reaches the payload.
        Assert.Equal("nl", session.DisplayLanguage);
        var add = Assert.IsType<DiagramAddDelta>(Assert.Single(baseline));
        var tea = Assert.Single(add.Elements, element => element.Id == "res:http://example.org/tea");
        Assert.Equal(SkosElementMapper.ConceptType, tea.Type);
        var payload = SkosConceptPayload.Parser.ParseFrom(tea.Payload.Span);
        Assert.Equal("Thee", payload.Label);
        Assert.Equal("nl", payload.LanguageTag);
        Assert.Equal((int)SkosLabelKind.Preferred, payload.LabelKind);
        Assert.Equal("T1", payload.Notation);

        var scheme = Assert.Single(add.Elements, element => element.Type == SkosElementMapper.SchemeType);
        Assert.Equal("res:http://example.org/scheme", scheme.Id);
    }

    [Fact]
    public void AMisplacedHeader_FallsBackToTheDefault_RatherThanGuessing()
    {
        // Arrange: language: above body: - the severing position; 2.1 ignores, 2.2 reports.
        var body = WriteBody();
        var registration = IoPath.Combine(_root, "drinks.adp");
        File.WriteAllText(registration, "w3c/skos\r\nlanguage: nl\r\nbody: drinks.ttl\r\n");

        // Act.
        var session = (SkosSession)Open(body, registration);

        // Assert.
        Assert.Equal(SkosLabels.DefaultLanguage, session.DisplayLanguage);
    }

    [Fact]
    public void BothReadings_ShareOneStoreEntry_ForOneFile()
    {
        // Arrange: the multi-registration promise - one parse, however many readings.
        var body = WriteBody();
        var store = _provider.GetRequiredService<IRdfDocumentStore>();

        // Act: load through the scheme reading, then ask the store as the graph reading would.
        var session = Open(body, registrationPath: null);
        _ = session.Baseline();
        var entry = store.GetOrLoad(body);

        // Assert: the same parsed entry serves both - reference-equal, not merely equivalent.
        Assert.Same(entry, store.GetOrLoad(body));
        Assert.True(entry.IsUsable);
    }

    [Fact]
    public async Task TheRefusals_NameTheirReasons()
    {
        // Arrange.
        var body = WriteBody();
        var readOnly = new SkosSession(body, registrationPath: null, "en",
            _provider.GetRequiredService<IRdfDocumentStore>(), _provider.GetRequiredService<SkosElementMapper>());
        var unregistered = Open(body, registrationPath: null);
        var registration = IoPath.Combine(_root, "drinks.adp");
        File.WriteAllText(registration, "w3c/skos\r\nbody: drinks.ttl\r\n");
        var registered = Open(body, registration);

        // Act & assert.
        Assert.Contains("read-only", await readOnly.MoveElementToAsync("res:http://example.org/tea", 1, 2, TestContext.Current.CancellationToken), StringComparison.Ordinal);
        Assert.Contains("Register the file", await unregistered.MoveElementToAsync("res:http://example.org/tea", 1, 2, TestContext.Current.CancellationToken), StringComparison.Ordinal);
        Assert.Contains("blank node", await registered.MoveElementToAsync("blank:0", 1, 2, TestContext.Current.CancellationToken), StringComparison.Ordinal);
        Assert.Contains("not something", await registered.MoveElementToAsync("edge:x|y|z", 1, 2, TestContext.Current.CancellationToken), StringComparison.Ordinal);
    }
}
