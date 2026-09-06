using EtAlii.Adp.Backend;
using EtAlii.Adp.Common;
using EtAlii.Adp.Diagram;
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

        // Add suggests this reading exactly where the marker triple is (Requirement 2.3), and
        // stays quiet on a file that is RDF but not a vocabulary.
        Assert.NotNull(Diagram.Skos.SuggestsBody);
        Assert.True(Diagram.Skos.SuggestsBody!("ex:s a skos:ConceptScheme ."));
        Assert.True(Diagram.Skos.SuggestsBody!("<http://x> a <http://www.w3.org/2004/02/skos/core#ConceptScheme> ."));
        Assert.False(Diagram.Skos.SuggestsBody!("ex:alice foaf:knows ex:bob ."));
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
        await File.WriteAllTextAsync(registration, "w3c/skos\r\nbody: drinks.ttl\r\n", TestContext.Current.CancellationToken);
        var registered = Open(body, registration);

        // Act & assert.
        Assert.Contains("read-only", await readOnly.MoveElementToAsync("res:http://example.org/tea", 1, 2, TestContext.Current.CancellationToken), StringComparison.Ordinal);
        Assert.Contains("Register the file", await unregistered.MoveElementToAsync("res:http://example.org/tea", 1, 2, TestContext.Current.CancellationToken), StringComparison.Ordinal);
        Assert.Contains("blank node", await registered.MoveElementToAsync("blank:0", 1, 2, TestContext.Current.CancellationToken), StringComparison.Ordinal);
        Assert.Contains("not something", await registered.MoveElementToAsync("edge:x|y|z", 1, 2, TestContext.Current.CancellationToken), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AViewportChange_AddsWhatCameIntoViewAndRemovesWhatLeft()
    {
        // Arrange.
        // The behavioural definition of the mechanism (view-delta-adoption Requirement 1.3): a
        // view CHANGE produces deltas. This is the test that fails against a session whose
        // UpdateView returns an empty list, which no client-side assertion can catch.
        var body = WriteBody();
        await using var session = Open(body, null);
        var drawn = ElementsOfBaseline(session);
        var anchor = drawn.OrderBy(element => element.Y).ThenBy(element => element.X).First();

        // Act.
        var narrowed = session.UpdateView(new DiagramViewport(anchor.X, anchor.Y, anchor.X + 1, anchor.Y + 1));
        var widened = session.UpdateView(DiagramViewport.Unbounded);

        // Assert.
        var removed = narrowed.OfType<DiagramRemoveDelta>().SelectMany(delta => delta.ElementIds).ToHashSet(StringComparer.Ordinal);
        Assert.NotEmpty(removed);
        Assert.DoesNotContain(anchor.Id, removed);

        var restored = widened.OfType<DiagramAddDelta>().SelectMany(delta => delta.Elements).Select(element => element.Id).ToHashSet(StringComparer.Ordinal);
        Assert.Subset(restored, removed);

        // Add before Remove - what the reference implementations emit, and what this module's
        // own mapper already produced. Requirement 4.3 anticipated the opposite.
        var kinds = widened.Select(delta => delta is DiagramAddDelta ? 0 : 1).ToList();
        Assert.Equal(kinds.OrderBy(kind => kind).ToList(), kinds);
    }

    [Fact]
    public async Task AnUnchangedViewport_SaysNothingTwice()
    {
        // Arrange.
        // A settled view that has not moved is not news; without this, any re-render that
        // re-reported the same rectangle would re-send the whole diagram.
        var body = WriteBody();
        await using var session = Open(body, null);
        session.UpdateView(DiagramViewport.Unbounded);

        // Act.
        var again = session.UpdateView(DiagramViewport.Unbounded);

        // Assert.
        Assert.Empty(again);
    }

    [Fact]
    public async Task PanningDoesNotMoveTheElementsItBringsIntoView()
    {
        // Arrange.
        // The layout is computed over the whole document and only then filtered. Computed over
        // the visible set instead, it would repack as the reader panned and the diagram would
        // crawl under them.
        var body = WriteBody();
        await using var session = Open(body, null);
        var atOpen = ElementsOfBaseline(session).ToDictionary(element => element.Id, element => (element.X, element.Y), StringComparer.Ordinal);

        // Act.
        session.UpdateView(new DiagramViewport(0, 0, 1, 1));
        var readmitted = session.UpdateView(DiagramViewport.Unbounded)
            .OfType<DiagramAddDelta>()
            .SelectMany(delta => delta.Elements)
            .ToList();

        // Assert.
        Assert.NotEmpty(readmitted);
        foreach (var element in readmitted)
        {
            Assert.Equal(atOpen[element.Id], (element.X, element.Y));
        }
    }

    /// <summary>The baseline's elements - what the connection holds before any view is reported.</summary>
    private static IReadOnlyList<DiagramElement> ElementsOfBaseline(IDiagramSession session) =>
        session.Baseline().OfType<DiagramAddDelta>().SelectMany(delta => delta.Elements).ToList();

    [Fact]
    public async Task TheVendoredThesaurus_NeverStrandsARelatedLink()
    {
        // Arrange.
        // Real data rather than a fixture, because the question is about a shape a fixture would
        // not have: business-economics.ttl carries 725 skos:related links, which are the
        // cross-links no layering rule places. Agent 4 raised them as a candidate for the
        // placeholder-position trap - an element the layout never placed sits at (0, 0) and a
        // point-in-rectangle filter keeps it only while the reader looks at the top-left corner.
        //
        // They are not at risk, and this pins the two reasons rather than leaving them to be
        // re-reasoned. A related link is an EDGE, produced only when both of its concepts are in
        // the file; and edges are filtered structurally here, on whether both endpoints survived,
        // never on a position of their own.
        var source = IoPath.Combine(ThesaurusFolder(), "business-economics.ttl");
        var body = IoPath.Combine(_root, "business-economics.ttl");
        File.Copy(source, body);

        await using var session = Open(body, null);
        var held = session.Baseline()
            .OfType<DiagramAddDelta>()
            .SelectMany(delta => delta.Elements)
            .ToDictionary(element => element.Id, element => element.Type, StringComparer.Ordinal);

        var edgesAtOpen = held.Count(entry => entry.Key.StartsWith("edge:", StringComparison.Ordinal));
        Assert.True(edgesAtOpen > 0, "the thesaurus must draw edges for this to mean anything");

        // Act.
        // A window over one concept's own cell, then the whole plane again - the pan that would
        // expose a filter judging edges by position.
        var anchor = session.Baseline()
            .OfType<DiagramAddDelta>()
            .SelectMany(delta => delta.Elements)
            .Where(element => element.Type == SkosElementMapper.ConceptType)
            .OrderBy(element => element.Y)
            .ThenBy(element => element.X)
            .First();

        foreach (var viewport in new[]
        {
            new DiagramViewport(anchor.X, anchor.Y, anchor.X + 1, anchor.Y + 1),
            new DiagramViewport(-50_000, -50_000, -49_000, -49_000),
            DiagramViewport.Unbounded,
        })
        {
            foreach (var delta in session.UpdateView(viewport))
            {
                switch (delta)
                {
                    case DiagramAddDelta add:
                        foreach (var element in add.Elements)
                        {
                            held[element.Id] = element.Type;
                        }

                        break;
                    case DiagramRemoveDelta remove:
                        foreach (var id in remove.ElementIds)
                        {
                            held.Remove(id);
                        }

                        break;
                }
            }

            // Assert.
            // Whatever the viewport, every edge the connection holds has both of its concepts.
            foreach (var edge in held.Keys.Where(id => id.StartsWith("edge:", StringComparison.Ordinal)).ToList())
            {
                var parts = edge["edge:".Length..].Split('|');
                Assert.Contains(parts[0], held.Keys);
                Assert.Contains(parts[2], held.Keys);
            }
        }
    }

    [Fact]
    public void TheThesaurusLayout_PlacesEveryConceptItDraws()
    {
        // Arrange.
        // The other half of the same question, and the one that would actually have been a bug:
        // an unplaced concept. The layout files concepts by scheme, sweeps the unfiled into a
        // band of their own, and places every collection - so nothing is left without a
        // position. If that ever stops being true, an unplaced concept is admitted by every
        // viewport rather than culled by all of them, which is the safe direction but not a
        // silent one.
        var text = File.ReadAllText(IoPath.Combine(ThesaurusFolder(), "business-economics.ttl"));
        var model = RdfParser.Parse(RdfDocument.Parse(text));
        var projection = SkosProjection.Project(model, int.MaxValue);

        // Act.
        var positions = SkosLayout.Layout(projection).Positions;

        // Assert.
        var unplaced = projection.Concepts.Select(concept => concept.Id)
            .Concat(projection.Schemes.Select(scheme => scheme.Id))
            .Concat(projection.Collections.Select(collection => collection.Id))
            .Where(id => !positions.ContainsKey(id))
            .ToList();
        Assert.Empty(unplaced);
    }

    /// <summary>The vendored STW thesaurus, found by walking up from the test binary.</summary>
    private static string ThesaurusFolder()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = IoPath.Combine(directory.FullName, "src", "diagrams", "rdf", "examples", "stw");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("src/diagrams/rdf/examples/stw was not found above the test binary.");
    }
}
