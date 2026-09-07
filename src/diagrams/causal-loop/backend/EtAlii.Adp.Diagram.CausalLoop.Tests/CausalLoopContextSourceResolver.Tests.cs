using EtAlii.Adp.Common;
using EtAlii.Adp.Common.Wire;
using EtAlii.Adp.Diagram;
using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.History;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.CausalLoop.Tests;

/// <summary>
/// What makes a variable, a link or a loop selectable at all.
/// </summary>
/// <remarks>
/// <para>
/// <b>This module shipped without a resolver, and the whole context surface was unreachable.</b>
/// A canvas selection resolves level by level - project, folders, the <c>.adp</c>, and then the
/// element inside it - and that last step is each diagram type's own. With no resolver the
/// element level never resolved, so no <see cref="ContextTarget"/> was ever built, so neither the
/// action provider nor the property provider was ever consulted. No context menu, nothing showing
/// as selected, no property grid.
/// </para>
/// <para>
/// <b>Sixteen provider tests passed the whole time.</b> They construct a target by hand and ask
/// the provider directly, which skips precisely the step that did not exist. So these tests do
/// not construct a target: they start from a file level, the way the running application does,
/// and let the resolver produce one.
/// </para>
/// </remarks>
public class CausalLoopContextSourceResolverTests : IDisposable
{
    private const string Body =
        "causal-loop 1\r\n"
        + "variable population \"Population\"\r\n"
        + "variable births \"Births\"\r\n"
        + "link population -> births +\r\n"
        + "link births -> population +\r\n"
        + "loop R1 \"Births beget births\" population births\r\n";

    private readonly string _workspace = IoPath.Combine(
        IoPath.GetTempPath(), "adp-cld-context-" + Guid.NewGuid().ToString("N"));

    private readonly CausalLoopDocumentStore _store = new();
    private readonly CausalLoopElementMapper _mapper = new();

    public CausalLoopContextSourceResolverTests() => Directory.CreateDirectory(_workspace);

    public void Dispose()
    {
        TestFolder.TryDelete(_workspace);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Writes the pair a reader actually opens: a registration beside its body.
    /// </summary>
    /// <remarks>
    /// The registration is the point. A canvas selection's parent level is the <c>.adp</c>, and
    /// the target has to carry the <c>.cld</c>, because that is what every command, provider and
    /// document store in this module reads. A test that started from the body would never
    /// exercise that hop.
    /// </remarks>
    private string WritePair(string name = "feedback")
    {
        var body = IoPath.Combine(_workspace, name + Diagram.DocumentExtension);
        var registration = IoPath.Combine(_workspace, name + ".adp");

        File.WriteAllText(body, Body);
        File.WriteAllText(registration, "systems/causal-loop-diagram\r\n");
        _store.Forget(body);

        return registration;
    }

    private CausalLoopContextSourceResolver Resolver() =>
        new(new DiagramFileRouter(new CausalLoopOnlyCatalog()), _store, _mapper);

    private static ContextResolvedLevel FileLevel(string path) =>
        new(
            ContextSelectionSource.Explorer,
            new ContextSource(),
            [IoPath.GetFileName(path)],
            ContextScope.Hierarchy,
            new ContextTarget(
                ContextScope.Hierarchy, path, IsContainer: false, SourceId: default,
                IoPath.GetDirectoryName(path)!, ShortGuid.NewShortGuid()),
            new ContextLevelDetail(),
            null!);

    private async Task<ContextLevelResolution> ResolveAsync(
        ContextResolvedLevel? parent, string elementId, IReadOnlyList<string>? clientPath = null) =>
        await Resolver().ResolveAsync(
            ShortGuid.NewShortGuid(),
            _workspace,
            ContextSelectionSource.DiagramCanvas,
            new ContextSource { ElementId = new ElementId { Value = elementId } },
            clientPath ?? [],
            parent,
            CancellationToken.None);

    // ---- the registration, which is the thing that was missing --------------------------------

    /// <summary>
    /// The guard for the defect itself: the module must register a resolver, not merely have one.
    /// </summary>
    /// <remarks>
    /// A class nothing resolves is a class that does not run. This is the assertion whose absence
    /// let an entire context surface ship unreachable, and it is deliberately about registration
    /// rather than about behaviour.
    /// </remarks>
    [Fact]
    public void TheModule_RegistersAnElementSourceResolver()
    {
        // Arrange.
        // The two services the host registers rather than this module: the viewport registry
        // from AddDiagrams and the router from AddHierarchy. Supplied here so that resolving
        // this module's registrations exercises the same graph the service builds.
        using var provider = new ServiceCollection()
            .AddSingleton<IDiagramViewportRegistry, DiagramViewportRegistry>()
            .AddSingleton<DiagramFileRouter>()
            .AddSingleton<IDiagramDefinitionCatalog, CausalLoopOnlyCatalog>()
            .AddCommands().AddHierarchyCommandHandlers()
            .AddCausalLoop()
            .BuildServiceProvider();

        // Act & assert.
        Assert.Contains(
            provider.GetServices<IContextSourceResolver>(),
            resolver => resolver is CausalLoopContextSourceResolver);
    }

    [Fact]
    public void ItOnlyClaimsElementIds()
    {
        // Act & assert.
        Assert.True(Resolver().CanResolve(new ContextSource { ElementId = new ElementId { Value = "variable:population" } }));
        Assert.False(Resolver().CanResolve(new ContextSource()));
    }

    // ---- each drawn kind resolves --------------------------------------------------------------

    [Fact]
    public async Task AVariable_Resolves_WithItsLabelAndItsPayload()
    {
        // Act.
        var resolution = await ResolveAsync(FileLevel(WritePair()), "variable:population");

        // Assert.
        var level = Assert.IsType<ResolvedContextLevel>(resolution).Level;
        Assert.Equal(ContextScope.DiagramElement, level.Scope);
        Assert.Equal(["Population"], level.RelativePath);
        Assert.Equal("variable:population", level.Target.ElementId);

        // The payload comes from the mapper, so the selection and the stream cannot describe one
        // element two different ways.
        Assert.NotEqual("", level.Detail.Element.ElementType);
        Assert.NotNull(level.Detail.Element.Payload);
    }

    [Fact]
    public async Task ALink_Resolves_NamedByItsTwoEnds()
    {
        // Act.
        var resolution = await ResolveAsync(FileLevel(WritePair()), "link:population|births");

        // Assert.
        var level = Assert.IsType<ResolvedContextLevel>(resolution).Level;
        Assert.Equal(["population → births"], level.RelativePath);
    }

    [Fact]
    public async Task ALoop_Resolves_WithItsCaption()
    {
        // Act.
        var resolution = await ResolveAsync(FileLevel(WritePair()), "loop:R1");

        // Assert.
        var level = Assert.IsType<ResolvedContextLevel>(resolution).Level;
        Assert.Equal(["R1 · Births beget births"], level.RelativePath);
    }

    /// <summary>
    /// The placement, which is what makes the diagram-wide actions reachable: with nothing
    /// selected the diagram itself is the subject, and a right-click on empty canvas carries the
    /// point through a channel that has one element id and no other field.
    /// </summary>
    [Fact]
    public async Task APlacement_Resolves_SoTheDiagramWideActionsCanBeOffered()
    {
        // Act.
        var resolution = await ResolveAsync(FileLevel(WritePair()), CausalLoopSelection.PlacementFor(120, 240));

        // Assert.
        var level = Assert.IsType<ResolvedContextLevel>(resolution).Level;
        Assert.Equal(ContextScope.DiagramElement, level.Scope);
        Assert.StartsWith("new:", level.Target.ElementId, StringComparison.Ordinal);

        // It names nothing in the document, so it carries no payload - and must not be mistaken
        // for an element that has gone.
        Assert.Equal(["This diagram"], level.RelativePath);
    }

    // ---- the hop that a hand-built target would have skipped ------------------------------------

    /// <summary>
    /// The registration is selected; the body is what the target carries.
    /// </summary>
    /// <remarks>
    /// Everything downstream - the document store, both providers, every command - is written
    /// against the <c>.cld</c>. A target carrying the <c>.adp</c> would make all of them refuse,
    /// silently, which is the shape of failure this module already shipped once.
    /// </remarks>
    [Fact]
    public async Task TheTarget_CarriesTheBody_NotTheRegistrationThatWasSelected()
    {
        // Arrange.
        var registration = WritePair();

        // Act.
        var resolution = await ResolveAsync(FileLevel(registration), "variable:births");

        // Assert.
        var level = Assert.IsType<ResolvedContextLevel>(resolution).Level;
        Assert.EndsWith(Diagram.DocumentExtension, level.Target.ResolvedFullPath, StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual(registration, level.Target.ResolvedFullPath);

        // And it is this module's origin, so the action resolver knows whose element it is.
        Assert.Equal(ServiceCollectionAddCausalLoopExtension.CausalLoopOrigin, level.Target.Origin);
    }

    /// <summary>
    /// The provider that could not be reached before can be reached now, from a target this
    /// resolver produced rather than one a test wrote by hand.
    /// </summary>
    [Fact]
    public async Task AResolvedTarget_IsOneTheActionProviderAnswersFor()
    {
        // Arrange.
        var resolution = await ResolveAsync(FileLevel(WritePair()), "variable:population");
        var target = Assert.IsType<ResolvedContextLevel>(resolution).Level.Target;

        var actions = new CausalLoopContextActionProvider(
            _store, new ServiceCollection().AddCommands().AddHierarchyCommandHandlers().BuildServiceProvider().GetRequiredService<IHistoryStackStore>(),
            new DiagramViewportRegistry());

        // Act.
        var groups = await actions.DiscoverAsync(target, TestContext.Current.CancellationToken);

        // Assert.
        // The end-to-end claim, in one line: a selection made the way the application makes one
        // reaches this module's actions.
        Assert.NotEmpty(groups.SelectMany(group => group.Actions));
    }

    // ---- refusals, so other modules' resolvers can answer ---------------------------------------

    [Fact]
    public async Task AnElementSelectedOutsideADiagram_IsRefused()
    {
        // Act.
        var resolution = await ResolveAsync(parent: null, "variable:population");

        // Assert.
        Assert.IsType<RejectedContextLevel>(resolution);
    }

    /// <summary>
    /// Every diagram type answers for an element id, and none can tell from the id alone whose
    /// element it is - only the file above says. Declining lets the next resolver answer.
    /// </summary>
    [Fact]
    public async Task AFileOfAnotherType_IsDeclined()
    {
        // Arrange.
        var other = IoPath.Combine(_workspace, "notes.md");
        await File.WriteAllTextAsync(other, Body, TestContext.Current.CancellationToken);

        // Act.
        var resolution = await ResolveAsync(FileLevel(other), "variable:population");

        // Assert.
        var rejected = Assert.IsType<RejectedContextLevel>(resolution);
        Assert.Contains("not a causal loop diagram", rejected.Reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// A diagram of another type is declined, so that module's own resolver can answer.
    /// </summary>
    /// <remarks>
    /// The fixture is a genuinely routable document of a different origin. An unroutable one -
    /// a <c>.md</c> - is turned away by the router before the origin is compared, so it would
    /// pass with the origin check deleted. This one does not.
    /// </remarks>
    [Fact]
    public async Task ADiagramOfAnotherType_IsDeclined_SoItsOwnResolverCanAnswer()
    {
        // Arrange.
        var other = IoPath.Combine(_workspace, "elsewhere" + TwoTypeCatalog.Other.Extension);
        await File.WriteAllTextAsync(other, Body, TestContext.Current.CancellationToken);

        var resolver = new CausalLoopContextSourceResolver(
            new DiagramFileRouter(new TwoTypeCatalog()), _store, _mapper);

        // Act.
        var resolution = await resolver.ResolveAsync(
            ShortGuid.NewShortGuid(),
            _workspace,
            ContextSelectionSource.DiagramCanvas,
            new ContextSource { ElementId = new ElementId { Value = "variable:population" } },
            [],
            FileLevel(other),
            CancellationToken.None);

        // Assert.
        var rejected = Assert.IsType<RejectedContextLevel>(resolution);
        Assert.Contains("not a causal loop diagram", rejected.Reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// An element nested under anything but a file is refused.
    /// </summary>
    /// <remarks>
    /// The scope half of that rule, which a null parent does not reach: with no file level above
    /// it there is nothing to verify the element against, and an unverifiable selection is never
    /// recorded.
    /// </remarks>
    [Fact]
    public async Task AnElementNestedUnderAnotherElement_IsRefused()
    {
        // Arrange.
        var registration = WritePair();
        var elementLevel = new ContextResolvedLevel(
            ContextSelectionSource.DiagramCanvas,
            new ContextSource(),
            ["Population"],
            ContextScope.DiagramElement,
            new ContextTarget(
                ContextScope.DiagramElement, registration, IsContainer: false, SourceId: default,
                _workspace, ShortGuid.NewShortGuid(), "variable:population"),
            new ContextLevelDetail(),
            null!);

        // Act.
        var resolution = await ResolveAsync(elementLevel, "variable:births");

        // Assert.
        var rejected = Assert.IsType<RejectedContextLevel>(resolution);
        Assert.Contains("within its diagram", rejected.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnElementTheDocumentNoLongerHolds_IsRefused()
    {
        // Act.
        var resolution = await ResolveAsync(FileLevel(WritePair()), "variable:nowhere");

        // Assert.
        var rejected = Assert.IsType<RejectedContextLevel>(resolution);
        Assert.Contains("no longer in this diagram", rejected.Reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// The client's path is checked, never trusted: it is what the client believes it selected,
    /// and the id is what it actually selected.
    /// </summary>
    [Fact]
    public async Task APathThatDisagreesWithTheElement_IsRefused()
    {
        // Act.
        var resolution = await ResolveAsync(FileLevel(WritePair()), "variable:population", ["Something else"]);

        // Assert.
        Assert.IsType<RejectedContextLevel>(resolution);
    }
}

/// <summary>A catalog of this module's types alone, so the router routes only what it owns.</summary>
internal sealed class CausalLoopOnlyCatalog : IDiagramDefinitionCatalog
{
    public IReadOnlyList<DiagramDefinition> All => Diagram.Definitions;
}

/// <summary>
/// This module's types plus one belonging to somebody else.
/// </summary>
/// <remarks>
/// Needed to test the origin check at all. A fixture of an unroutable type - a <c>.md</c>, say -
/// is declined by the router before the origin is ever compared, so it passes whether or not the
/// check is there. Found by sabotage: removing the origin comparison broke nothing until this
/// existed.
/// </remarks>
internal sealed class TwoTypeCatalog : IDiagramDefinitionCatalog
{
    /// <summary>Another module's type, as far as the router is concerned.</summary>
    public static DiagramDefinition Other { get; } = new(
        new DiagramOrigin("someone-else", "other-diagram"),
        "Other diagram",
        "Another module's type, used to check that this module declines what is not its own.",
        Icon: "mdi-file",
        Extension: ".other",
        Build: null);

    public IReadOnlyList<DiagramDefinition> All { get; } = [.. Diagram.Definitions, Other];
}
