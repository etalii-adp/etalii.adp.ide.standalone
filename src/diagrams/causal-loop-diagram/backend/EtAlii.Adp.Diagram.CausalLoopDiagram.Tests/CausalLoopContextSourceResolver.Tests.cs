using System.Text.RegularExpressions;
using EtAlii.Adp.Context;
using EtAlii.Adp.Diagram.Modules;
using EtAlii.Adp.Documents.Wire;
using EtAlii.Adp.Files;
using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.History;
using EtAlii.Adp.Sessions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.CausalLoopDiagram.Tests;

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
/// <para>
/// <b>What every element resolver must do is the shared contract's</b>
/// (<see cref="DiagramElementSourceResolverContract"/>), on the on-call example and through its
/// registration; the facts below are this module's own.
/// </para>
/// </remarks>
public sealed class CausalLoopContextSourceResolverTests : DiagramElementSourceResolverContract
{
    private const string Body =
        "causal-loop 1\r\n"
        + "variable population \"Population\"\r\n"
        + "variable births \"Births\"\r\n"
        + "link population -> births +\r\n"
        + "link births -> population +\r\n"
        + "loop R1 \"Births beget births\" population births\r\n";

    private readonly CausalLoopDocumentStore _store = new(new FileWriter());
    private readonly CausalLoopElementMapper _mapper = new();

    protected override string Example => CausalLoopDocumentStoreTests.OnCall;

    protected override IContextSourceResolver CreateResolver() => Resolver();

    protected override void Reload(string body) => _store.Reload(body);

    protected override string ElementId => "variable:incidents";

    protected override IReadOnlyList<string> ElementPath => ["Incidents"];

    protected override string ElementType => CausalLoopElementMapper.VariableType;

    protected override string Gesture => CausalLoopSelection.RelationFor("incidents", "onCallLoad");

    protected override DiagramElementTexts Texts { get; } = new(
        "The selected file is not a causal loop diagram.",
        "That element is no longer in this diagram.",
        "This diagram",
        "New link");

    /// <summary>A selection is made in the registration, and the target carries the body beside it.</summary>
    protected override string SelectedIn(string body)
    {
        var registration = IoPath.ChangeExtension(body, ".adp");
        File.WriteAllText(registration, "systems/causal-loop-diagram\r\n");
        return registration;
    }

    /// <summary>A variable is named by the quoted label on its <c>variable</c> line, and keeps its id through a rename.</summary>
    protected override async Task<IReadOnlyList<string>> RenameAsync(string body, string name, CancellationToken cancellationToken)
    {
        var text = await File.ReadAllTextAsync(body, cancellationToken);
        var renamed = Regex.Replace(text, "(?<=^variable incidents \")[^\"]*", name, RegexOptions.Multiline, TimeSpan.FromSeconds(1));
        Assert.NotEqual(text, renamed);
        await File.WriteAllTextAsync(body, renamed, cancellationToken);
        return [name];
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
    private string WritePair(TestFolder workspace, string name = "feedback")
    {
        var body = workspace.Combine(name + Diagram.DocumentExtension);
        var registration = workspace.Combine(name + ".adp");

        File.WriteAllText(body, Body);
        File.WriteAllText(registration, "systems/causal-loop-diagram\r\n");
        _store.Forget(body);

        return registration;
    }

    private CausalLoopContextSourceResolver Resolver() =>
        new(new DiagramFileRouter(new CausalLoopOnlyCatalog()), _store, _mapper);

    private static ContextResolvedLevel FileLevel(string path) =>
        new(
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
            IoPath.GetDirectoryName(parent!.Target.ResolvedFullPath)!,
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
    public async Task ALink_Resolves_NamedByItsTwoEnds()
    {
        // Arrange.
        using var workspace = TestFolder.Create("causal-loop-resolver");

        // Act.
        var resolution = await ResolveAsync(FileLevel(WritePair(workspace)), "link:population|births");

        // Assert.
        var level = Assert.IsType<ResolvedContextLevel>(resolution).Level;
        Assert.Equal(["population → births"], level.RelativePath);
    }

    [Fact]
    public async Task ALoop_Resolves_WithItsCaption()
    {
        // Arrange.
        using var workspace = TestFolder.Create("causal-loop-resolver");

        // Act.
        var resolution = await ResolveAsync(FileLevel(WritePair(workspace)), "loop:R1");

        // Assert.
        var level = Assert.IsType<ResolvedContextLevel>(resolution).Level;
        Assert.Equal(["R1 · Births beget births"], level.RelativePath);
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
        using var workspace = TestFolder.Create("causal-loop-resolver");
        var registration = WritePair(workspace);

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
        using var workspace = TestFolder.Create("causal-loop-resolver");
        var resolution = await ResolveAsync(FileLevel(WritePair(workspace)), "variable:population");
        var target = Assert.IsType<ResolvedContextLevel>(resolution).Level.Target;

        var actions = new CausalLoopContextActionProvider(
            _store, new ServiceCollection().AddCommands().AddHierarchyCommandHandlers().BuildServiceProvider().GetRequiredService<IHistoryStackStore>(),
            new DiagramViewportRegistry());

        // Act.
        var groups = await actions.DiscoverAsync(target, TestContext.Current.CancellationToken);

        // Assert.
        // The end-to-end claim, in one line: a selection made the way the application makes one
        // reaches this module's actions.
        Assert.NotEmpty(groups.Actions());
    }

    // ---- refusals, so other modules' resolvers can answer ---------------------------------------

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
        using var workspace = TestFolder.Create("causal-loop-resolver");
        var other = workspace.Combine("elsewhere" + TwoTypeCatalog.Other.Extension);
        await File.WriteAllTextAsync(other, Body, TestContext.Current.CancellationToken);

        var resolver = new CausalLoopContextSourceResolver(
            new DiagramFileRouter(new TwoTypeCatalog()), _store, _mapper);

        // Act.
        var resolution = await resolver.ResolveAsync(
            ShortGuid.NewShortGuid(),
            workspace.Path,
            new ContextSource { ElementId = new ElementId { Value = "variable:population" } },
            [],
            FileLevel(other),
            CancellationToken.None);

        // Assert.
        var rejected = Assert.IsType<RejectedContextLevel>(resolution);
        Assert.Contains("not a causal loop diagram", rejected.Reason, StringComparison.Ordinal);
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
        new ToolOrigin("someone-else", "other-diagram"),
        "Other diagram",
        "Another module's type, used to check that this module declines what is not its own.",
        Icon: "mdi-file",
        Extension: ".other",
        Build: null);

    public IReadOnlyList<DiagramDefinition> All { get; } = [.. Diagram.Definitions, Other];
}
