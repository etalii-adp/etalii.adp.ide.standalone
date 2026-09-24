using EtAlii.Adp.Documents;
using EtAlii.Adp.Documents.Wire;
using EtAlii.Adp.Context;
using EtAlii.Adp.Hierarchy;
using Xunit;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Diagram.WardleyMap.Tests;

/// <summary>
/// Making an element selectable - and refusing to record a selection that cannot be verified
/// (Requirements 11.1-11.3).
/// </summary>
public sealed class WardleyContextSourceResolverTests : IDisposable
{
    private const string Map = """
        title Tea Shop
        anchor Business [0.95, 0.63]
        component Cup of Tea [0.79, 0.61]
        component Kettle [0.43, 0.35] (buy)
        component Power [0.1, 0.7]
        Cup of Tea->Kettle
        Kettle+>Power
        pipeline Kettle
        {
          component Electric Kettle [0.45]
        }
        note This is a note [0.2, 0.2]
        pioneers [0.30, 0.20, 0.55, 0.45]

        """;

    /// <summary>
    /// What separates the parts of a composite identity key. Named here as well, because a
    /// composite key written out as one run of text reads as if it had no separator at all -
    /// which is exactly the misreading <see cref="WardleyIdentityKeys.Separator"/> is documented
    /// against.
    /// </summary>
    private const char Key = WardleyIdentityKeys.Separator;

    private readonly string _root = IoPath.Combine(IoPath.GetTempPath(), $"wardley-resolver-{Guid.NewGuid():N}");
    private readonly WardleyDocumentStore _documents = new();
    private readonly WardleyContextSourceResolver _resolver;
    private readonly string _registration;
    private readonly string _body;

    public WardleyContextSourceResolverTests()
    {
        Directory.CreateDirectory(_root);
        _registration = IoPath.Combine(_root, "tea.adp");
        _body = IoPath.Combine(_root, "tea.owm");
        File.WriteAllText(_registration, "wardley/map\n");
        File.WriteAllText(_body, Map);

        // A mindmap definition beside this module's own, so the "not ours" path has a real
        // other type to be rejected in favour of rather than a hypothetical one.
        var catalog = new WardleyTestDiagramDefinitionCatalog(
            Diagram.WardleyMap,
            new DiagramDefinition(new DiagramOrigin("freeplane", "mindmap"), "Mind map", Extension: ".mm"));

        _resolver = new WardleyContextSourceResolver(
            new DiagramFileRouter(catalog),
            _documents,
            new WardleyElementMapper(new WardleyIdentities()));
    }

    public void Dispose()
    {
        TestFolder.TryDelete(_root);
    }

    // ---- resolving ----------------------------------------------------------------------------

    [Fact]
    public void ItAnswersForElementIds_AndNothingElse()
    {
        // Arrange, act and assert. Requirement 11.1: the existing member is reused rather than
        // a Wardley-shaped variant being added to the contract.
        Assert.True(_resolver.CanResolve(new ContextSource { ElementId = new ElementId { Value = "x" } }));
        Assert.False(_resolver.CanResolve(new ContextSource { EntryId = ShortGuid.NewShortGuid() }));
    }

    [Fact]
    public async Task AComponent_ResolvesToItsOwnNameAndPath()
    {
        // Act.
        var resolution = await Resolve(IdOf(WardleyIdentityKind.Component, "Kettle"));

        // Assert.
        var level = Assert.IsType<ResolvedContextLevel>(resolution).Level;
        Assert.Equal(ContextScope.DiagramElement, level.Scope);
        Assert.Equal(["Kettle"], level.RelativePath);
        Assert.Equal("Kettle", level.Detail.Element.Text);
        Assert.Equal(_body, level.Target.ResolvedFullPath);
    }

    [Fact]
    public async Task AComponentThatIsAPipelineParent_SaysItHasChildren()
    {
        // Act.
        var kettle = Assert.IsType<ResolvedContextLevel>(await Resolve(IdOf(WardleyIdentityKind.Component, "Kettle"))).Level;
        var power = Assert.IsType<ResolvedContextLevel>(await Resolve(IdOf(WardleyIdentityKind.Component, "Power"))).Level;

        // Assert. The pipeline is the only containment this notation has, so it is the only
        // thing `has_children` can mean here.
        Assert.True(kettle.Detail.Element.HasChildren);
        Assert.False(power.Detail.Element.HasChildren);
    }

    [Fact]
    public async Task APipelineChild_IsNamedBeneathItsParent()
    {
        // Act.
        var resolution = await Resolve(IdOf(WardleyIdentityKind.PipelineChild, $"Kettle{Key}Electric Kettle"));

        // Assert. The one two-segment path this notation produces.
        var level = Assert.IsType<ResolvedContextLevel>(resolution).Level;
        Assert.Equal(["Kettle", "Electric Kettle"], level.RelativePath);
        Assert.Equal("Electric Kettle", level.Detail.Element.Text);
    }

    [Fact]
    public async Task ALink_IsDescribedByWhatItJoins()
    {
        // Act. A link has no name of its own; its key is its endpoints run together, which is a
        // handle rather than something to show anyone.
        var dependency = await Resolve(IdOf(WardleyIdentityKind.Link, $"Cup of Tea{Key}Kettle{Key}{WardleyLinkKind.Dependency}"));
        var flow = await Resolve(IdOf(WardleyIdentityKind.Link, $"Kettle{Key}Power{Key}{WardleyLinkKind.Flow}"));

        // Assert.
        Assert.Equal("Cup of Tea -> Kettle", Assert.IsType<ResolvedContextLevel>(dependency).Level.Detail.Element.Text);
        Assert.Equal("Kettle +> Power", Assert.IsType<ResolvedContextLevel>(flow).Level.Detail.Element.Text);
    }

    [Fact]
    public async Task ANoteAndARegion_AreSelectableToo()
    {
        // Act.
        var note = await Resolve(IdOf(WardleyIdentityKind.Note, "This is a note"));
        var attitude = await Resolve(IdOf(WardleyIdentityKind.Attitude, $"Pioneers{Key}0.3{Key}0.2"));

        // Assert.
        Assert.Equal("This is a note", Assert.IsType<ResolvedContextLevel>(note).Level.Detail.Element.Text);
        Assert.Equal("Pioneers", Assert.IsType<ResolvedContextLevel>(attitude).Level.Detail.Element.Text);
    }

    [Fact]
    public async Task TheSelectionCarriesTheElementsOwnPayload()
    {
        // Act.
        var level = Assert.IsType<ResolvedContextLevel>(await Resolve(IdOf(WardleyIdentityKind.Component, "Kettle"))).Level;

        // Assert. Requirement 11.3: the pushed selection is self-describing - a consumer reads
        // the kind, the coordinates and the derived stage without asking a second time.
        Assert.Equal(WardleyElementTypes.Element, level.Detail.Element.ElementType);
        var payload = level.Detail.Element.Payload.Unpack<WardleyElementPayload>();
        Assert.Equal("Kettle", payload.Name);
        Assert.Equal(WardleyElementKind.Component, payload.Kind);
        Assert.Equal(0.43, payload.Visibility, 6);
        Assert.Equal(0.35, payload.Maturity, 6);
        Assert.Equal("Custom Built", payload.EvolutionStage);
    }

    [Fact]
    public async Task TheDeliveredPayload_IsTheOneTheCanvasAlreadyHas()
    {
        // Arrange. Two descriptions of one element that could drift apart is exactly the defect
        // this shares the mapper to avoid.
        var identities = _documents.Identities(_body);
        var map = WardleyParser.Parse(_documents.GetOrLoad(_body));
        var elementId = IdOf(WardleyIdentityKind.Component, "Cup of Tea");
        var streamed = new WardleyElementMapper(new WardleyIdentities())
            .Elements(map, identities)
            .Single(element => element.Id == elementId);

        // Act.
        var level = Assert.IsType<ResolvedContextLevel>(await Resolve(elementId)).Level;

        // Assert.
        Assert.Equal(streamed.Type, level.Detail.Element.ElementType);
        Assert.Equal(streamed.Payload.ToArray(), level.Detail.Element.Payload.Value.ToByteArray());
    }

    [Fact]
    public async Task NothingNestsInsideAnElement()
    {
        // Act.
        var level = Assert.IsType<ResolvedContextLevel>(await Resolve(IdOf(WardleyIdentityKind.Component, "Kettle"))).Level;

        // Assert.
        Assert.Equal(ContextNesting.NotNestable, _resolver.NestingOf(level));
    }

    // ---- what it refuses ----------------------------------------------------------------------

    [Fact]
    public async Task AnElementWithNoDiagramAboveIt_IsRejected()
    {
        // Act. An unverifiable selection is never recorded.
        var resolution = await _resolver.ResolveAsync(
            ShortGuid.NewShortGuid(), _root, ContextSelectionSource.DiagramCanvas,
            new ContextSource { ElementId = new ElementId { Value = "whatever" } },
            [], parent: null, TestContext.Current.CancellationToken);

        // Assert.
        Assert.Contains("within its diagram", Assert.IsType<RejectedContextLevel>(resolution).Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnUnknownElement_IsRejected()
    {
        // Act.
        var resolution = await Resolve("not-an-id");

        // Assert.
        Assert.Contains("no longer on this map", Assert.IsType<RejectedContextLevel>(resolution).Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheEvolutionAxis_IsNotSelectable()
    {
        // Act. It is on the element stream, but it is the chrome the map is drawn against
        // rather than a thing the map contains.
        var resolution = await Resolve(WardleyElementTypes.EvolutionAxisId);

        // Assert.
        Assert.IsType<RejectedContextLevel>(resolution);
    }

    [Fact]
    public async Task AClientPathThatDisagreesWithTheElement_IsRejected()
    {
        // Act. The client's version of the path is checked, never trusted.
        var resolution = await Resolve(IdOf(WardleyIdentityKind.Component, "Kettle"), clientPath: ["Power"]);

        // Assert.
        Assert.Contains("does not match", Assert.IsType<RejectedContextLevel>(resolution).Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AFileOfAnotherType_IsRejectedPlainly()
    {
        // Arrange. Every diagram type answers for element_id and none can tell from the id
        // alone whose element it is, so saying "not ours" plainly is how they coexist: the
        // selection resolver then asks the next one.
        var other = IoPath.Combine(_root, "notes.adp");
        await File.WriteAllTextAsync(other, "freeplane/mindmap\n", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(IoPath.Combine(_root, "notes.mm"), "<map><node TEXT=\"a\"/></map>", TestContext.Current.CancellationToken);

        // Act.
        var resolution = await Resolve(IdOf(WardleyIdentityKind.Component, "Kettle"), registration: other);

        // Assert.
        Assert.Contains("not a Wardley map", Assert.IsType<RejectedContextLevel>(resolution).Reason, StringComparison.Ordinal);
    }

    // ---- tracking -----------------------------------------------------------------------------

    [Fact]
    public async Task ARemovedElement_ClearsTheSelection()
    {
        // Arrange.
        var level = Assert.IsType<ResolvedContextLevel>(await Resolve(IdOf(WardleyIdentityKind.Component, "Power"))).Level;
        IReadOnlyList<string>? announced = ["unset"];
        using var subscription = _resolver.Track(ShortGuid.NewShortGuid(), _root, level, path => announced = path);

        // Act.
        var document = _documents.GetOrLoad(_body);
        WardleyWriter.RemoveLine(document, WardleyParser.Parse(document).Components.Single(c => c.Name == "Power").Line);
        _documents.Save(_body, document);

        // Assert.
        Assert.Null(announced);
    }

    [Fact]
    public async Task ARename_AnnouncesTheNewPath_RatherThanClearingTheSelection()
    {
        // Arrange. The identity survives a rename (Requirement 4.4), so what the tracker sees
        // is a path change - not the element vanishing.
        var level = Assert.IsType<ResolvedContextLevel>(await Resolve(IdOf(WardleyIdentityKind.Component, "Power"))).Level;
        IReadOnlyList<string>? announced = null;
        using var subscription = _resolver.Track(ShortGuid.NewShortGuid(), _root, level, path => announced = path);

        // Act.
        var document = _documents.GetOrLoad(_body);
        WardleyWriter.Rename(document, WardleyParser.Parse(document), "Power", "Mains Power");
        _documents.Rekey(_body, WardleyIdentityKind.Component, "Power", "Mains Power");
        _documents.Save(_body, document);

        // Assert.
        Assert.Equal(["Mains Power"], announced);
    }

    [Fact]
    public async Task ADisposedSubscription_StopsAnnouncing()
    {
        // Arrange.
        var level = Assert.IsType<ResolvedContextLevel>(await Resolve(IdOf(WardleyIdentityKind.Component, "Power"))).Level;
        var announcements = 0;
        var subscription = _resolver.Track(ShortGuid.NewShortGuid(), _root, level, _ => announcements++);

        // Act.
        subscription.Dispose();
        _documents.Touch(_body);

        // Assert.
        Assert.Equal(0, announcements);
    }

    // ---- plumbing -----------------------------------------------------------------------------

    private string IdOf(string kind, string key) =>
        _documents.Identities(_body).Single(entry => entry.Kind == kind && entry.Key == key).Id;

    private async Task<ContextLevelResolution> Resolve(
        string elementId,
        IReadOnlyList<string>? clientPath = null,
        string? registration = null)
    {
        var parent = new ContextResolvedLevel(
            ContextSelectionSource.Explorer,
            new ContextSource { EntryId = ShortGuid.NewShortGuid() },
            [],
            ContextScope.Hierarchy,
            new ContextTarget(ContextScope.Hierarchy, registration ?? _registration, IsContainer: false, SourceId: default, _root),
            new ContextLevelDetail(),
            null!);

        return await _resolver.ResolveAsync(
            ShortGuid.NewShortGuid(), _root, ContextSelectionSource.DiagramCanvas,
            new ContextSource { ElementId = new ElementId { Value = elementId } },
            clientPath ?? [], parent, TestContext.Current.CancellationToken);
    }
}
