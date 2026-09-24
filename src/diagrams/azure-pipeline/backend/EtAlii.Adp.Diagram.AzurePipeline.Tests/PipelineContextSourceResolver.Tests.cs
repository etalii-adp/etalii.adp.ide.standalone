using EtAlii.Adp.Documents;
using EtAlii.Adp.Documents.Wire;
using EtAlii.Adp.Context;
using EtAlii.Adp.Hierarchy;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.AzurePipeline.Tests;

/// <summary>
/// What makes a pipeline element selectable. The whole of it is registering a resolver - the
/// context service is untouched (Requirement 12.2) - so what is worth testing is that it agrees
/// the file is a pipeline, that it verifies rather than trusts what the client sent, and that it
/// resolves the detail the property grid will need without a second lookup.
/// </summary>
public class PipelineContextSourceResolverTests : IDisposable
{
    private readonly string _workspace = IoPath.Combine(
        IoPath.GetTempPath(),
        "adp-pipeline-context-" + Guid.NewGuid().ToString("N"));

    private readonly PipelineDocumentStore _store = new();

    public PipelineContextSourceResolverTests() => Directory.CreateDirectory(_workspace);

    public void Dispose()
    {
        TestFolder.TryDelete(_workspace);

        GC.SuppressFinalize(this);
    }

    private const string Pipeline = """
        stages:
          - stage: Build
            displayName: Build the solution
            jobs:
              - job: Compile
                steps:
                  - script: dotnet build
                    displayName: Compile it
          - stage: Test
            jobs:
              - job: Verify
                steps:
                  - script: dotnet test
        """;

    /// <summary>
    /// Writes a pipeline and the registration that opens it, and returns the registration.
    /// </summary>
    /// <remarks>
    /// The <c>.adp</c> is not decoration here. This module declares <c>.yml</c> as a
    /// <b>shared</b> extension, so a bare body is deliberately not routable - too many things own
    /// a .yml for guessing to be safe - and a pipeline becomes a diagram by being registered.
    /// A selection therefore always sits inside the registration, which is what the resolver is
    /// handed as its parent level.
    /// </remarks>
    private string Write(string name, string content)
    {
        var body = IoPath.Combine(_workspace, name + Diagram.DocumentExtension);
        File.WriteAllText(body, content);

        var registration = IoPath.Combine(_workspace, name + ".adp");
        File.WriteAllText(registration, Diagram.Pipeline.Origin.MimeType + "\n");
        return registration;
    }

    /// <summary>
    /// A catalog carrying only this module's definition, so the router routes the fixture files
    /// without the process-wide discovery cache being filled by a test.
    /// </summary>
    private PipelineContextSourceResolver Resolver() =>
        new(new DiagramFileRouter(new PipelineOnlyCatalog()), _store);

    /// <summary>The pipeline a registration opens.</summary>
    private static string BodyOf(string registrationPath) =>
        IoPath.ChangeExtension(registrationPath, Diagram.DocumentExtension);

    /// <summary>The file level a pipeline element is selected inside.</summary>
    private static ContextResolvedLevel FileLevel(string path) =>
        new(
            ContextSelectionSource.Explorer,
            new ContextSource(),
            [IoPath.GetFileName(path)],
            ContextScope.Hierarchy,
            new ContextTarget(ContextScope.Hierarchy, path, IsContainer: false, SourceId: default, IoPath.GetDirectoryName(path)!, ShortGuid.NewShortGuid()),
            new ContextLevelDetail(),
            null!);

    private Task<ContextLevelResolution> ResolveAsync(
        string path,
        string elementId,
        IReadOnlyList<string>? clientPath = null) =>
        ResolveWithinAsync(FileLevel(path), elementId, clientPath);

    /// <summary>Resolves against whatever parent level the caller wants - including none.</summary>
    private async Task<ContextLevelResolution> ResolveWithinAsync(
        ContextResolvedLevel? parent,
        string elementId,
        IReadOnlyList<string>? clientPath = null)
    {
        var id = new ContextSource { ElementId = new ElementId { Value = elementId } };
        return await Resolver().ResolveAsync(
            ShortGuid.NewShortGuid(),
            _workspace,
            ContextSelectionSource.DiagramCanvas,
            id,
            clientPath ?? [],
            parent,
            CancellationToken.None);
    }

    [Fact]
    public void ItOnlyClaimsElementIds()
    {
        // Arrange & act & assert.
        // A resolver that claimed everything would take entries away from the hierarchy's own.
        Assert.True(Resolver().CanResolve(new ContextSource { ElementId = new ElementId { Value = "Build" } }));
        Assert.False(Resolver().CanResolve(new ContextSource()));
    }

    [Fact]
    public async Task AStage_ResolvesToItsOwnName()
    {
        // Arrange.
        var path = Write("azure-pipelines", Pipeline);

        // Act.
        var resolution = await ResolveAsync(path, "Build");

        // Assert.
        var resolved = Assert.IsType<ResolvedContextLevel>(resolution);
        Assert.Equal(["Build the solution"], resolved.Level.RelativePath);
        Assert.Equal("Build the solution", resolved.Level.Detail.Element.Text);
        Assert.Equal(ContextScope.DiagramElement, resolved.Level.Scope);
    }

    [Fact]
    public async Task AJob_ResolvesToItsStageAndItself()
    {
        // Arrange: the path is what identifies the element to a person, and a job called Verify
        // in two different stages is two different jobs.
        var path = Write("azure-pipelines", Pipeline);

        // Act.
        var resolution = await ResolveAsync(path, "Build/Compile");

        // Assert.
        var resolved = Assert.IsType<ResolvedContextLevel>(resolution);
        Assert.Equal(["Build the solution", "Compile"], resolved.Level.RelativePath);
    }

    [Fact]
    public async Task AStep_ResolvesThroughItsJob()
    {
        // Arrange & act.
        var path = Write("azure-pipelines", Pipeline);
        var resolution = await ResolveAsync(path, "Build/Compile/step-0");

        // Assert.
        var resolved = Assert.IsType<ResolvedContextLevel>(resolution);
        Assert.Equal(["Build the solution", "Compile", "Compile it"], resolved.Level.RelativePath);
    }

    [Fact]
    public async Task AStageWithJobs_IsAContainer()
    {
        // Arrange & act.
        var path = Write("azure-pipelines", Pipeline);
        var resolution = await ResolveAsync(path, "Build");

        // Assert.
        var resolved = Assert.IsType<ResolvedContextLevel>(resolution);
        Assert.True(resolved.Level.Detail.Element.HasChildren);
        Assert.Equal(ContextNesting.Contained, Resolver().NestingOf(resolved.Level));
    }

    [Fact]
    public async Task AStep_HoldsNothing()
    {
        // Arrange: steps are the bottom of this model - there is nothing inside one to select.
        var path = Write("azure-pipelines", Pipeline);
        var resolution = await ResolveAsync(path, "Build/Compile/step-0");

        // Assert.
        var resolved = Assert.IsType<ResolvedContextLevel>(resolution);
        Assert.False(resolved.Level.Detail.Element.HasChildren);
        Assert.Equal(ContextNesting.NotNestable, Resolver().NestingOf(resolved.Level));
    }

    [Fact]
    public async Task AnElementSelectedOutsideItsDiagram_IsRejected()
    {
        // Arrange: the pipeline exists and the element in it is real - what is missing is the
        // file level, and with nothing to verify the element against it is refused anyway.
        Write("azure-pipelines", Pipeline);

        // Act.
        var resolution = await ResolveWithinAsync(parent: null, "Build");

        // Assert.
        var rejected = Assert.IsType<RejectedContextLevel>(resolution);
        Assert.Contains("within its pipeline", rejected.Reason);
    }

    [Fact]
    public async Task AnUnknownElement_IsRejected()
    {
        // Arrange & act.
        var path = Write("azure-pipelines", Pipeline);
        var resolution = await ResolveAsync(path, "NoSuchStage");

        // Assert.
        Assert.IsType<RejectedContextLevel>(resolution);
    }

    [Fact]
    public async Task APathThatDoesNotMatchTheElement_IsRejected()
    {
        // Arrange: the client's path is checked, never trusted - it is the client that would be
        // out of date after a rename, and acting on its version would select the wrong thing.
        var path = Write("azure-pipelines", Pipeline);

        // Act.
        var resolution = await ResolveAsync(path, "Build", ["Something Else"]);

        // Assert.
        var rejected = Assert.IsType<RejectedContextLevel>(resolution);
        Assert.Contains("does not match", rejected.Reason);
    }

    [Fact]
    public async Task AFileThatDoesNotParse_SaysSoRatherThanClaimingTheElementIsUnknown()
    {
        // Arrange: the canvas may not have caught up, and "this file does not parse" is a more
        // useful answer than "no such element".
        var path = Write("broken", "stages:\n  - stage: Build\n   jobs: [\n");

        // Act.
        var resolution = await ResolveAsync(path, "Build");

        // Assert.
        var rejected = Assert.IsType<RejectedContextLevel>(resolution);
        Assert.Contains("does not parse", rejected.Reason);
    }

    [Fact]
    public async Task AnElementCarriesItsOwnTarget_SoAConsumerNeedsNoSecondLookup()
    {
        // Arrange: Requirement 12.3 - the property grid shows what is selected without going and
        // asking again.
        var path = Write("azure-pipelines", Pipeline);

        // Act.
        var resolution = await ResolveAsync(path, "Build/Compile");

        // Assert.
        var resolved = Assert.IsType<ResolvedContextLevel>(resolution);
        Assert.Equal("Build/Compile", resolved.Level.Target.ElementId);
        // The target is the pipeline itself, not the registration that opened it: an edit has to
        // land in the YAML, and the .adp carries only the MIME line.
        Assert.Equal(BodyOf(path), resolved.Level.Target.ResolvedFullPath);
    }

    [Fact]
    public async Task AnElementFromATemplate_IsMarkedAsLinked()
    {
        // Arrange: its text lives in another file, so it is a pointer somewhere else as much as a
        // thing in its own right (Requirement 5.4).
        Directory.CreateDirectory(IoPath.Combine(_workspace, "templates"));
        await File.WriteAllTextAsync(IoPath.Combine(_workspace, "templates", "build.yml"), "jobs:\n  - job: Compile\n    steps:\n      - script: x\n", TestContext.Current.CancellationToken);
        var path = Write("azure-pipelines", "stages:\n  - stage: Build\n    jobs:\n      - template: templates/build.yml\n");

        // Act.
        var resolution = await ResolveAsync(path, "Build/Compile");

        // Assert.
        var resolved = Assert.IsType<ResolvedContextLevel>(resolution);
        Assert.True(resolved.Level.Detail.Element.Linked);
    }

    [Fact]
    public async Task ARenamedElement_IsReResolvedToItsNewPath()
    {
        // Arrange: a rename changes the path, and a selection showing the old one is a selection
        // pointing at nothing.
        var path = Write("azure-pipelines", Pipeline);
        var resolution = await ResolveAsync(path, "Build");
        var level = Assert.IsType<ResolvedContextLevel>(resolution).Level;
        IReadOnlyList<string>? reported = null;
        using var tracking = Resolver().Track(ShortGuid.NewShortGuid(), _workspace, level, updated => reported = updated);

        // Act.
        var entry = _store.GetOrLoad(_workspace, BodyOf(path));
        var stage = entry.Model.Stages.Single(candidate => candidate.Name == "Build");
        new PipelineWriter(entry.Document).SetDisplayName(PipelineEditTarget.For(stage), "Built");
        _store.Save(_workspace, BodyOf(path), entry);

        // Assert.
        Assert.Equal(["Built"], reported);
    }

    [Fact]
    public async Task ADeletedElement_ClearsTheSelection()
    {
        // Arrange: a null path is how the context mechanism is told the thing is gone.
        var path = Write("azure-pipelines", Pipeline);
        var level = Assert.IsType<ResolvedContextLevel>(await ResolveAsync(path, "Test")).Level;
        var reported = new List<IReadOnlyList<string>?>();
        using var tracking = Resolver().Track(ShortGuid.NewShortGuid(), _workspace, level, updated => reported.Add(updated));

        // Act.
        await File.WriteAllTextAsync(BodyOf(path), "stages:\n  - stage: Build\n    jobs:\n      - job: Compile\n        steps:\n          - script: x\n", TestContext.Current.CancellationToken);
        _store.Reload(_workspace, BodyOf(path));

        // Assert.
        Assert.Single(reported);
        Assert.Null(reported[0]);
    }

    [Fact]
    public async Task ADisposedTracker_HearsNothingMore()
    {
        // Arrange: a selection that never unsubscribes keeps a whole document graph alive.
        var path = Write("azure-pipelines", Pipeline);
        var level = Assert.IsType<ResolvedContextLevel>(await ResolveAsync(path, "Build")).Level;
        var reported = 0;
        var tracking = Resolver().Track(ShortGuid.NewShortGuid(), _workspace, level, _ => reported++);

        // Act.
        tracking.Dispose();
        _store.Touch(_workspace, BodyOf(path));

        // Assert.
        Assert.Equal(0, reported);
    }

    [Fact]
    public async Task AChangeToAnotherFile_IsNotThisSelectionsBusiness()
    {
        // Arrange.
        var mine = Write("azure-pipelines", Pipeline);
        var theirs = Write("other", Pipeline);
        var level = Assert.IsType<ResolvedContextLevel>(await ResolveAsync(mine, "Build")).Level;
        var reported = 0;
        using var tracking = Resolver().Track(ShortGuid.NewShortGuid(), _workspace, level, _ => reported++);

        // Act.
        _store.GetOrLoad(_workspace, BodyOf(theirs));
        _store.Touch(_workspace, BodyOf(theirs));

        // Assert.
        Assert.Equal(0, reported);
    }
    // ---- an arrow is selectable too ---------------------------------------------------------

    /// <summary>
    /// Every kind of arrow this canvas draws: an implicit stage order (Test after Build), an
    /// explicit dependsOn (Deploy on Test), a broken one (Deploy on a stage that does not exist),
    /// and a job-level one (Pack on Compile).
    /// </summary>
    private const string Arrows = """
        stages:
          - stage: Build
            jobs:
              - job: Compile
                steps:
                  - script: dotnet build
              - job: Pack
                dependsOn: Compile
                steps:
                  - script: dotnet pack
          - stage: Test
            jobs:
              - job: Verify
                steps:
                  - script: dotnet test
          - stage: Deploy
            dependsOn:
              - Test
              - Nowhere
            jobs:
              - job: Ship
                steps:
                  - script: ./ship
        """;

    /// <summary>
    /// The id of every arrow the canvas draws for this pipeline, taken from the module's own graph
    /// and <see cref="PipelineElementMapper.EdgeId"/> - never a re-spelled format, which could only
    /// agree with the mapper by coincidence.
    /// </summary>
    private IReadOnlyList<PipelineEdge> DrawnEdges(string registration)
    {
        var model = _store.GetOrLoad(_workspace, BodyOf(registration)).Model;
        return
        [
            .. PipelineGraphBuilder.OfStages(model).Edges,
            .. model.Stages.SelectMany(stage => PipelineGraphBuilder.OfJobs(stage).Edges),
        ];
    }

    [Fact]
    public async Task EveryArrowTheCanvasDraws_Resolves_ImplicitExplicitBrokenAndJobLevel()
    {
        // Arrange: the library pushes a pressed arrow's own id, and the highlight follows the
        // backend's answer - so an arrow this resolver cannot name is an arrow that can never be
        // selected (centralized-selection Requirements 2.1, 2.2).
        var path = Write("azure-pipelines", Arrows);
        var edges = DrawnEdges(path);
        Assert.Contains(edges, edge => edge.IsImplicit);
        Assert.Contains(edges, edge => edge.IsBroken);
        Assert.Contains(edges, edge => !edge.IsImplicit && !edge.IsBroken && edge.ToId == "Deploy");
        Assert.Contains(edges, edge => edge.ToId == "Build/Pack");

        // Act & assert.
        foreach (var edge in edges)
        {
            var id = PipelineElementMapper.EdgeId(edge);
            var resolved = Assert.IsType<ResolvedContextLevel>(await ResolveAsync(path, id));
            Assert.False(resolved.Level.Target.IsContainer);
            Assert.Equal(id, resolved.Level.Target.ElementId);
        }
    }

    [Theory]
    [InlineData("Test", "Deploy", new[] { "Deploy" }, "Deploy waits for Test")]
    [InlineData("Build", "Test", new[] { "Test" }, "Test waits for Build")]
    [InlineData("", "Deploy", new[] { "Deploy" }, "Deploy waits for Nowhere")]
    [InlineData("Build/Compile", "Build/Pack", new[] { "Build", "Pack" }, "Pack waits for Compile")]
    public async Task AnArrow_AnswersAsTheElementThatWaits(string fromId, string toId, string[] expectedPath, string expectedText)
    {
        // Arrange: an arrow is the waiting element's dependency - it is written in THAT element's
        // dependsOn, or implied by its place - so the waiting side is what it answers as, the way
        // ansible's edges answer as their declaring side.
        var path = Write("azure-pipelines", Arrows);
        var edge = DrawnEdges(path).Single(candidate => candidate.FromId == fromId && candidate.ToId == toId);

        // Act.
        var resolved = Assert.IsType<ResolvedContextLevel>(await ResolveAsync(path, PipelineElementMapper.EdgeId(edge)));

        // Assert.
        Assert.Equal(expectedPath, resolved.Level.RelativePath);
        Assert.Equal(expectedText, resolved.Level.Detail.Element.Text);
        Assert.False(resolved.Level.Detail.Element.HasChildren);
    }

    [Fact]
    public async Task AnArrowThatIsNotDrawn_IsStillRejected()
    {
        // Arrange: resolving arrows must not turn "any id shaped like an arrow" into a selection.
        var path = Write("azure-pipelines", Arrows);

        // Act.
        var resolution = await ResolveAsync(path, "edge:Deploy->Build");

        // Assert.
        Assert.IsType<RejectedContextLevel>(resolution);
    }

    [Fact]
    public async Task ASelectedArrow_SurvivesAnUnrelatedEdit_AndClearsWhenItGoes()
    {
        // Arrange: Track shares Find, so an arrow is followed the way an element is (3.3).
        var path = Write("azure-pipelines", Arrows);
        var edge = DrawnEdges(path).Single(candidate => candidate.FromId == "Test" && candidate.ToId == "Deploy");
        var level = Assert.IsType<ResolvedContextLevel>(await ResolveAsync(path, PipelineElementMapper.EdgeId(edge))).Level;
        var reported = new List<IReadOnlyList<string>?>();
        using var tracking = Resolver().Track(ShortGuid.NewShortGuid(), _workspace, level, updated => reported.Add(updated));

        // Act: an edit elsewhere, then one that removes the dependency.
        _store.Touch(_workspace, BodyOf(path));
        await File.WriteAllTextAsync(BodyOf(path), Arrows.Replace("- Test", "", StringComparison.Ordinal), TestContext.Current.CancellationToken);
        _store.Reload(_workspace, BodyOf(path));

        // Assert.
        Assert.Equal(2, reported.Count);
        Assert.Equal(["Deploy"], reported[0]);
        Assert.Null(reported[1]);
    }
}

/// <summary>
/// The definitions the router should know about in these tests: this module's, and nothing else.
/// </summary>
/// <remarks>
/// Using the real catalog would fill the process-wide discovery cache from whatever assemblies
/// happen to be beside the test binary, which makes the outcome depend on the deployment rather
/// than on the test.
/// </remarks>
internal sealed class PipelineOnlyCatalog : IDiagramDefinitionCatalog
{
    public IReadOnlyList<DiagramDefinition> All => Diagram.Definitions;
}
