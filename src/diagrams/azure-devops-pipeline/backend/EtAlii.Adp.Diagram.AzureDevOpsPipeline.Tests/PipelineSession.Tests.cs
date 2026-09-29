using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.AzureDevOpsPipeline.Tests;

/// <summary>
/// One open diagram for one connection. Two things are worth holding: the document is shared and
/// the expansion is not - two people looking at one pipeline see the same stages, but which of
/// them each has opened is a property of looking rather than of the file - and a change of any
/// shape reaches the connection as deltas, including the removals, since a stage somebody deleted
/// would otherwise stay on the canvas because nothing mentioned it.
/// </summary>
public class PipelineSessionTests : IDisposable
{
    private readonly string _workspace = IoPath.Combine(
        IoPath.GetTempPath(),
        "adp-pipeline-session-" + Guid.NewGuid().ToString("N"));

    private readonly PipelineDocumentStore _store = new();
    private readonly PipelineElementMapper _mapper = new(PipelineMetrics.Default);
    private readonly PipelineViewState _views = new();

    public PipelineSessionTests() => Directory.CreateDirectory(_workspace);

    public void Dispose()
    {
        TestFolder.TryDelete(_workspace);

        GC.SuppressFinalize(this);
    }

    private const string TwoStages = """
        stages:
          - stage: Build
            jobs:
              - job: Compile
                steps:
                  - script: x
              - job: Lint
                steps:
                  - script: y
          - stage: Test
            jobs:
              - job: Verify
                steps:
                  - script: z
        """;

    /// <summary>
    /// One job whose steps are declared in an order that is neither alphabetical nor sorted by
    /// kind, so "declared order" (Requirement 3.4) is what the assertion can be measuring.
    /// </summary>
    private const string StepsOutOfAlphabeticalOrder = """
        stages:
          - stage: Build
            jobs:
              - job: Compile
                steps:
                  - script: zebra
                    displayName: Zebra
                  - script: apple
                    displayName: Apple
                  - script: mango
                    displayName: Mango
              - job: Empty
        """;

    private string Write(string content)
    {
        var path = IoPath.Combine(_workspace, "azure-pipelines.yml");
        File.WriteAllText(path, content);
        return path;
    }

    private PipelineSession Open(string path) =>
        OpenWith(path, ShortGuid.NewShortGuid());

    /// <summary>A session on a known watch id, so a test can toggle that connection's own view.</summary>
    private PipelineSession OpenWith(string path, ShortGuid watchId) =>
        new(watchId, _workspace, path, _store, _mapper, _views);

    private static IEnumerable<string> AddedIds(IEnumerable<DiagramDelta> deltas) =>
        deltas.OfType<DiagramAddDelta>().SelectMany(delta => delta.Elements).Select(element => element.Id);

    private static IEnumerable<string> RemovedIds(IEnumerable<DiagramDelta> deltas) =>
        deltas.OfType<DiagramRemoveDelta>().SelectMany(delta => delta.ElementIds);

    [Fact]
    public async Task OpeningAPipeline_StreamsABaseline()
    {
        // Arrange.
        await using var session = Open(Write(TwoStages));

        // Act.
        var baseline = session.Baseline();

        // Assert.
        Assert.Contains("Build", AddedIds(baseline));
        Assert.Contains("Test", AddedIds(baseline));
        Assert.Contains("edge:Build->Test", AddedIds(baseline));
    }

    [Fact]
    public async Task ABaselineOfACollapsedPipeline_CarriesNoJobs()
    {
        // Arrange.
        await using var session = Open(Write(TwoStages));

        // Act.
        var baseline = session.Baseline();

        // Assert.
        Assert.DoesNotContain("Build/Compile", AddedIds(baseline));
    }

    [Fact]
    public async Task ExpandingAStage_PushesItsJobsWithoutRemovingAnything()
    {
        // Arrange: the toggle goes through the shared view state, which is the only thing a
        // context action can reach - and the session hears about it and pushes. Holding the
        // expansion inside the session instead left nothing able to change it, so no stage was
        // ever opened and a pipeline's jobs were unreachable.
        var path = Write(TwoStages);
        var watchId = ShortGuid.NewShortGuid();
        await using var session = OpenWith(path, watchId);
        session.Baseline();
        var pushed = new List<DiagramDeltasEventArgs>();
        session.Changed += (_, args) => pushed.Add(args);

        // Act.
        _views.Toggle(watchId, path, "Build");

        // Assert.
        var deltas = Assert.Single(pushed).Deltas;
        Assert.Contains("Build/Compile", AddedIds(deltas));
        Assert.Contains("Build/Lint", AddedIds(deltas));
        Assert.Empty(RemovedIds(deltas));
    }

    [Fact]
    public async Task CollapsingAStage_TakesItsJobsBackOff()
    {
        // Arrange.
        var path = Write(TwoStages);
        var watchId = ShortGuid.NewShortGuid();
        await using var session = OpenWith(path, watchId);
        session.Baseline();
        _views.Toggle(watchId, path, "Build");
        var pushed = new List<DiagramDeltasEventArgs>();
        session.Changed += (_, args) => pushed.Add(args);

        // Act.
        _views.Toggle(watchId, path, "Build");

        // Assert.
        Assert.Contains("Build/Compile", RemovedIds(Assert.Single(pushed).Deltas));
        Assert.False(session.IsExpanded("Build"));
    }

    [Fact]
    public async Task AToggleOnAnotherConnection_IsNotThisOnesBusiness()
    {
        // Arrange: which stages somebody has opened is a property of looking, not of the file.
        var path = Write(TwoStages);
        var mineId = ShortGuid.NewShortGuid();
        var yoursId = ShortGuid.NewShortGuid();
        await using var mine = OpenWith(path, mineId);
        await using var yours = OpenWith(path, yoursId);
        mine.Baseline();
        yours.Baseline();
        var pushedToYou = 0;
        yours.Changed += (_, _) => pushedToYou++;

        // Act.
        _views.Toggle(mineId, path, "Build");

        // Assert.
        Assert.True(mine.IsExpanded("Build"));
        Assert.False(yours.IsExpanded("Build"));
        Assert.Equal(0, pushedToYou);
        Assert.Equal(TwoStages, await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ADisposedSession_ForgetsWhatItHadOpen()
    {
        // Arrange: a connection that closed its diagram must not leave its expansion behind for
        // whoever is handed the same watch id next.
        var path = Write(TwoStages);
        var watchId = ShortGuid.NewShortGuid();
        var session = OpenWith(path, watchId);
        session.Baseline();
        _views.Toggle(watchId, path, "Build");

        // Act.
        await session.DisposeAsync();

        // Assert.
        Assert.False(_views.For(watchId, path).IsExpanded("Build"));
    }

    [Fact]
    public async Task AnEditThroughAnotherConnection_ReachesThisOne()
    {
        // Arrange: the store is shared, so an edit anywhere is an edit everywhere.
        var path = Write(TwoStages);
        await using var session = Open(path);
        session.Baseline();
        var pushed = new List<DiagramDeltasEventArgs>();
        session.Changed += (_, args) => pushed.Add(args);

        // Act.
        var entry = _store.GetOrLoad(_workspace, path);
        var build = entry.Model.Stages.Single(stage => stage.Name == "Build");
        new PipelineWriter(entry.Document).SetDisplayName(PipelineEditTarget.For(build), "Build it all");
        _store.Save(_workspace, path, entry);

        // Assert.
        var deltas = Assert.Single(pushed);
        Assert.Contains("Build", AddedIds(deltas.Deltas));
    }

    [Fact]
    public async Task AnEditedElement_ArrivesAsAnAddCarryingItsNewState()
    {
        // Arrange: Requirement 11.5 - an edit is an Add, not a new kind of delta. The element
        // keeps its id, so a session sending only what newly appeared would send nothing at all
        // and the canvas would still be showing the old name.
        var path = Write(TwoStages);
        await using var session = Open(path);
        session.Baseline();
        DiagramDeltasEventArgs? pushed = null;
        session.Changed += (_, args) => pushed = args;

        // Act.
        var entry = _store.GetOrLoad(_workspace, path);
        var build = entry.Model.Stages.Single(stage => stage.Name == "Build");
        new PipelineWriter(entry.Document).SetDisplayName(PipelineEditTarget.For(build), "Build it all");
        _store.Save(_workspace, path, entry);

        // Assert.
        Assert.NotNull(pushed);
        var element = pushed.Deltas.OfType<DiagramAddDelta>()
            .SelectMany(delta => delta.Elements)
            .Single(candidate => candidate.Id == "Build");
        var payload = PipelineElementPayload.Parser.ParseFrom(element.Payload.ToArray());
        Assert.Equal("Build it all", payload.DisplayName);
    }

    [Fact]
    public async Task AStageThatDisappears_IsRemovedRatherThanLeftOnTheCanvas()
    {
        // Arrange: nothing would mention it again, so without an explicit removal it would simply
        // stay there - the failure mode that makes re-delivering-everything not quite enough.
        var path = Write(TwoStages);
        await using var session = Open(path);
        session.Baseline();
        DiagramDeltasEventArgs? pushed = null;
        session.Changed += (_, args) => pushed = args;

        // Act.
        await File.WriteAllTextAsync(path, "stages:\n  - stage: Build\n    jobs:\n      - job: Compile\n        steps:\n          - script: x\n", TestContext.Current.CancellationToken);
        _store.Reload(_workspace, path);

        // Assert.
        Assert.NotNull(pushed);
        Assert.Contains("Test", RemovedIds(pushed.Deltas));
        Assert.Contains("edge:Build->Test", RemovedIds(pushed.Deltas));
    }

    [Fact]
    public async Task AnExternalEdit_PushesTheDifference()
    {
        // Arrange: a git pull, or somebody editing the file in their own editor.
        var path = Write(TwoStages);
        await using var session = Open(path);
        session.Baseline();
        DiagramDeltasEventArgs? pushed = null;
        session.Changed += (_, args) => pushed = args;

        // Act.
        await File.WriteAllTextAsync(path, TwoStages + "\n  - stage: Ship\n    jobs:\n      - job: Deploy\n        steps:\n          - script: q\n", TestContext.Current.CancellationToken);
        _store.Reload(_workspace, path);

        // Assert.
        Assert.NotNull(pushed);
        Assert.Contains("Ship", AddedIds(pushed.Deltas));
    }

    [Fact]
    public async Task AChangeToADifferentFile_IsNotThisSessionsBusiness()
    {
        // Arrange: the store serves every open pipeline, so a session has to filter by its own.
        var mine = Write(TwoStages);
        var theirs = IoPath.Combine(_workspace, "other.yml");
        await File.WriteAllTextAsync(theirs, TwoStages, TestContext.Current.CancellationToken);
        await using var session = Open(mine);
        session.Baseline();
        var pushed = 0;
        session.Changed += (_, _) => pushed++;

        // Act.
        _store.GetOrLoad(_workspace, theirs);
        _store.Touch(_workspace, theirs);

        // Assert.
        Assert.Equal(0, pushed);
    }

    [Fact]
    public async Task AChangeToADifferentFile_IsNotEvenRenderedForThisSession()
    {
        // Arrange: the shared diff sends only what differs, so a session that re-rendered on
        // another file's change would stay silent whenever its own view had not moved - and the
        // test above would pass against a session that ignored the path altogether. So this
        // session's view is changed WITHOUT telling it, by toggling the connection's view
        // directly rather than through the announcing view state: a session that re-rendered on
        // the other file's change would now push the stage's jobs.
        var mine = Write(TwoStages);
        var theirs = IoPath.Combine(_workspace, "other.yml");
        await File.WriteAllTextAsync(theirs, TwoStages, TestContext.Current.CancellationToken);
        var watchId = ShortGuid.NewShortGuid();
        await using var session = OpenWith(mine, watchId);
        session.Baseline();
        _views.For(watchId, mine).Toggle("Build");
        var pushed = new List<DiagramDeltasEventArgs>();
        session.Changed += (_, args) => pushed.Add(args);

        // Act.
        _store.GetOrLoad(_workspace, theirs);
        _store.Touch(_workspace, theirs);
        var pushedForTheirs = pushed.Count;
        _store.Touch(_workspace, mine);

        // Assert: nothing for the other file, and the same change to its own file does push the
        // jobs - which is what makes the silence mean something.
        Assert.Equal(0, pushedForTheirs);
        Assert.Contains("Build/Compile", AddedIds(Assert.Single(pushed).Deltas));
    }

    [Fact]
    public async Task ADisposedSession_HearsNothingMore()
    {
        // Arrange: a connection that closed its diagram must not keep the session alive through
        // the store's event, nor be pushed to after it has gone.
        var path = Write(TwoStages);
        var session = Open(path);
        session.Baseline();
        var pushed = 0;
        session.Changed += (_, _) => pushed++;

        // Act.
        await session.DisposeAsync();
        _store.Touch(_workspace, path);

        // Assert.
        Assert.Equal(0, pushed);
    }

    [Fact]
    public async Task AFileThatDoesNotParse_ShowsAsUnavailableRatherThanHalfDrawn()
    {
        // Arrange: Requirement 3.6. Drawing what could be read of a broken file would be worse
        // than drawing nothing, because it would look complete.
        await using var session = Open(Write("stages:\n  - stage: Build\n   jobs: [\n"));

        // Act.
        var baseline = session.Baseline();

        // Assert.
        Assert.Empty(baseline);
        Assert.Contains("Line ", session.Unavailable);
    }

    [Fact]
    public async Task AWorkingFile_ReportsNothingUnavailable()
    {
        // Arrange & act.
        await using var session = Open(Write(TwoStages));

        // Assert.
        Assert.Equal("", session.Unavailable);
    }

    [Fact]
    public async Task ReportingAViewport_DeliversTheWholePipeline()
    {
        // Arrange: Requirement 11.8 permits it explicitly - these are small graphs and filtering
        // has little to do.
        await using var session = Open(Write(TwoStages));
        session.Baseline();

        // Act.
        var deltas = session.UpdateView(new DiagramViewport(0, 0, 10, 10));

        // Assert.
        // Everything was already delivered, so a viewport this small still removes nothing.
        Assert.Empty(RemovedIds(deltas));
    }

    [Fact]
    public async Task DraggingAStage_IsRefusedWithAReasonRatherThanIgnored()
    {
        // Arrange: a pipeline has no coordinates, and inventing somewhere to keep them in an
        // executable build definition is the annotation Requirement 3.3 forbids.
        await using var session = Open(Write(TwoStages));

        // Act.
        var refusal = await session.MoveElementAsync("Build", "100,200", 0, CancellationToken.None);

        // Assert.
        Assert.Contains("dependencies", refusal);
        Assert.Equal(TwoStages, await File.ReadAllTextAsync(IoPath.Combine(_workspace, "azure-pipelines.yml"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ExpandingAJob_PushesItsStepsInDeclaredOrder()
    {
        // Arrange: the third level of Requirement 8.2 - a job in turn expandable to its steps -
        // and Requirement 3.4, which says steps are shown in their declared order because they
        // are a sequence and not a graph. The stage has to be open first: a job is only on the
        // canvas at all once its stage is showing its jobs, so the levels nest.
        var path = Write(StepsOutOfAlphabeticalOrder);
        var watchId = ShortGuid.NewShortGuid();
        await using var session = OpenWith(path, watchId);
        session.Baseline();
        _views.Toggle(watchId, path, "Build");
        var pushed = new List<DiagramDeltasEventArgs>();
        session.Changed += (_, args) => pushed.Add(args);

        // Act.
        _views.Toggle(watchId, path, "Build/Compile");

        // Assert: its three steps arrive, and in the order the file declares them rather than
        // any order the ids or labels would sort into.
        var deltas = Assert.Single(pushed).Deltas;
        var steps = AddedIds(deltas).Where(id => id.StartsWith("Build/Compile/", StringComparison.Ordinal)).ToArray();
        Assert.Equal(["Build/Compile/step-0", "Build/Compile/step-1", "Build/Compile/step-2"], steps);
        Assert.Empty(RemovedIds(deltas));
    }

    [Fact]
    public async Task CollapsingAJob_TakesExactlyItsStepsBackOff()
    {
        // Arrange: the assertion shape copied from the c4 fix at 350b8f9e - closing a level takes
        // back precisely what it revealed, and leaves the level above it alone.
        var path = Write(StepsOutOfAlphabeticalOrder);
        var watchId = ShortGuid.NewShortGuid();
        await using var session = OpenWith(path, watchId);
        session.Baseline();
        _views.Toggle(watchId, path, "Build");
        _views.Toggle(watchId, path, "Build/Compile");
        var pushed = new List<DiagramDeltasEventArgs>();
        session.Changed += (_, args) => pushed.Add(args);

        // Act.
        _views.Toggle(watchId, path, "Build/Compile");

        // Assert.
        var deltas = Assert.Single(pushed).Deltas;
        var removed = RemovedIds(deltas).ToArray();
        Assert.Equal(
            ["Build/Compile/step-0", "Build/Compile/step-1", "Build/Compile/step-2"],
            removed.Where(id => id.StartsWith("Build/Compile/", StringComparison.Ordinal)).Order(StringComparer.Ordinal));

        // The job itself, and its stage, stay: only what the job revealed goes back off.
        Assert.DoesNotContain("Build/Compile", removed);
        Assert.DoesNotContain("Build", removed);
    }

    [Fact]
    public async Task AnUnopenedJob_CarriesNoStepsOnTheWire()
    {
        // Arrange: a pipeline read at three levels at once is unreadable (Requirement 8.2), so an
        // open stage shows its jobs and nothing deeper until a job is opened too.
        var path = Write(StepsOutOfAlphabeticalOrder);
        var watchId = ShortGuid.NewShortGuid();
        await using var session = OpenWith(path, watchId);
        session.Baseline();
        var pushed = new List<DiagramDeltasEventArgs>();
        session.Changed += (_, args) => pushed.Add(args);

        // Act.
        _views.Toggle(watchId, path, "Build");

        // Assert.
        Assert.DoesNotContain(AddedIds(Assert.Single(pushed).Deltas), id => id.Contains("/step-", StringComparison.Ordinal));
    }
}
