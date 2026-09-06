using EtAlii.Adp.Backend;
using EtAlii.Adp.Common;
using EtAlii.Adp.Diagram;
using EtAlii.Adp.Hierarchy;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.Databricks.Tests;

/// <summary>
/// The session: each type opens from a registered fixture with computed layout, stored
/// positions win element by element, and a reposition is one undo away while the body file
/// never changes by a byte (databricks-diagrams Requirements 1.4, 7.1, 7.4, 7.6, 7.7).
/// </summary>
public class DatabricksSessionTests : IDisposable
{
    private readonly string _root;
    private readonly ServiceProvider _provider;

    public DatabricksSessionTests()
    {
        _root = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _provider = new ServiceCollection()
            .AddSingleton<IReadOnlyList<DiagramDefinition>>(Diagram.Definitions)
            .AddCommands()
            .AddDatabricks()
            .BuildServiceProvider();
    }

    public void Dispose()
    {
        _provider.Dispose();
        TestFolder.TryDelete(_root);
    }

    private string CopyFixture(string name)
    {
        var destination = IoPath.Combine(_root, name);
        File.Copy(IoPath.Combine(AppContext.BaseDirectory, "Fixtures", name), destination);
        return destination;
    }

    private string WriteRegistration(string mime, string bodyName, string extra = "")
    {
        var path = IoPath.Combine(_root, "plan.adp");
        File.WriteAllText(path, $"{mime}\r\nbody: {bodyName}\r\n{extra}");
        return path;
    }

    private IDiagramSession Open(string originKey, string bodyPath, string? registrationPath)
    {
        var factory = _provider.GetServices<IDiagramSessionFactory>()
            .Single(candidate => candidate.Origin.Key == originKey);
        return factory.Open(ShortGuid.NewShortGuid(), _root, bodyPath, registrationPath);
    }

    private static IReadOnlyList<DiagramElement> ElementsOf(IDiagramSession session)
    {
        var baseline = Assert.Single(session.Baseline());
        return Assert.IsType<DiagramAddDelta>(baseline).Elements.ToList();
    }

    [Fact]
    public async Task EachType_OpensFromARegisteredFixture_WithItsElements()
    {
        // Arrange.
        var job = CopyFixture("job.yml");
        var bundle = CopyFixture("bundle.yml");
        var pipeline = CopyFixture("pipeline.json");

        // Act & assert.
        await using (var session = Open("databricks/job", job, WriteRegistration("databricks/job", "job.yml")))
        {
            var elements = ElementsOf(session);
            Assert.Contains(elements, element => element.Id == "task:ingest");
            Assert.Contains(elements, element => element.Id == "edge:quality_gate->publish");
            Assert.Contains(elements, element => element.Id == "cluster:ingest_cluster");
        }

        await using (var session = Open("databricks/bundle", bundle, null))
        {
            var elements = ElementsOf(session);
            Assert.Contains(elements, element => element.Id == "bundle");
            Assert.Contains(elements, element => element.Id == "resource:pipelines/bronze_to_gold");
            Assert.Contains(elements, element => element.Id == "override:prod/jobs/nightly_ingest");
        }

        await using (var session = Open("databricks/pipeline", pipeline, null))
        {
            var elements = ElementsOf(session);
            Assert.Contains(elements, element => element.Id == "pipeline");
            Assert.Contains(elements, element => element.Id == "library:transformations/bronze");
            Assert.Contains(elements, element => element.Id == "flow:pipeline->target");
        }
    }

    [Fact]
    public async Task AStoredPosition_WinsOverTheComputedOne_ElementByElement()
    {
        // Arrange.
        var body = CopyFixture("job.yml");
        var adp = WriteRegistration("databricks/job", "job.yml", "layout:\r\n  task:ingest: 555 666\r\n");

        // Act.
        await using var session = Open("databricks/job", body, adp);
        var elements = ElementsOf(session);

        // Assert.
        var moved = elements.Single(element => element.Id == "task:ingest");
        Assert.Equal((555d, 666d), (moved.X, moved.Y));
        // Every other element keeps its computed place (Requirement 7.4).
        var untouched = elements.Single(element => element.Id == "task:quality_gate");
        Assert.NotEqual((555d, 666d), (untouched.X, untouched.Y));
    }

    [Fact]
    public async Task AReposition_LandsInTheAdp_LeavesTheBodyAlone_AndIsOneUndoAway()
    {
        // Arrange.
        var body = CopyFixture("job.yml");
        var bodyBytes = await File.ReadAllBytesAsync(body, TestContext.Current.CancellationToken);
        var adp = WriteRegistration("databricks/job", "job.yml");
        var adpBefore = await File.ReadAllTextAsync(adp, TestContext.Current.CancellationToken);

        // Act.
        await using var session = Open("databricks/job", body, adp);
        var refusal = await session.MoveElementToAsync("task:ingest", 120, 240, TestContext.Current.CancellationToken);

        // Assert.
        Assert.Equal("", refusal);
        Assert.Equal(new RegistrationPosition(120, 240), RegistrationLayout.Read(adp)["task:ingest"]);
        // The body file never changes by a byte (Requirement 7.7).
        Assert.Equal(bodyBytes, await File.ReadAllBytesAsync(body, TestContext.Current.CancellationToken));
        // And the drag is one undo away, returning the .adp byte for byte (Requirement 7.6).
        await _provider.GetRequiredService<IHistoryStackStore>().Get(_root)
            .UndoAsync(TestContext.Current.CancellationToken);
        Assert.Equal(adpBefore, await File.ReadAllTextAsync(adp, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task TheResourceHeader_PicksTheDeclaration_TheSessionShows()
    {
        // Arrange.
        // A file declaring two jobs: the resource: header names the second.
        var path = IoPath.Combine(_root, "jobs.yml");
        await File.WriteAllTextAsync(path, "resources:\r\n  jobs:\r\n"
            + "    first:\r\n      name: First\r\n      tasks:\r\n        - task_key: a\r\n"
            + "    second:\r\n      name: Second\r\n      tasks:\r\n        - task_key: b\r\n", TestContext.Current.CancellationToken);
        var adp = WriteRegistration("databricks/job", "jobs.yml", "resource: second\r\n");

        // Act.
        await using var session = Open("databricks/job", path, adp);
        var elements = ElementsOf(session);

        // Assert.
        Assert.Contains(elements, element => element.Id == "task:b");
        Assert.DoesNotContain(elements, element => element.Id == "task:a");
    }

    [Fact]
    public async Task AnEdge_CarriesItsEndpointsAndOutcome()
    {
        // Arrange.
        var body = CopyFixture("job.yml");

        // Act.
        await using var session = Open("databricks/job", body, WriteRegistration("databricks/job", "job.yml"));
        var elements = ElementsOf(session);

        // Assert.
        var edge = elements.Single(element => element.Id == "edge:quality_gate->publish");
        var payload = DatabricksEdgePayload.Parser.ParseFrom(edge.Payload.Span);
        Assert.Equal(("task:quality_gate", "task:publish", "true"),
            (payload.FromElementId, payload.ToElementId, payload.Outcome));
    }

    [Fact]
    public async Task AnEdge_RefusesToBeMoved()
    {
        // Arrange.
        var body = CopyFixture("job.yml");

        // Act.
        await using var session = Open("databricks/job", body, WriteRegistration("databricks/job", "job.yml"));
        var refusal = await session.MoveElementToAsync("edge:quality_gate->publish", 1, 2, TestContext.Current.CancellationToken);

        // Assert.
        Assert.Contains("not something this diagram can move", refusal, StringComparison.Ordinal);
    }

    /// <summary>
    /// The DAG laid out in one column, far enough apart that a viewport can hold exactly one
    /// task. Authored positions rather than the computed layout, so these tests describe the
    /// viewport rule and do not quietly also test <see cref="DatabricksJobLayout"/>.
    /// </summary>
    private const string SpreadOut = """
        layout:
          task:ingest: 0 0
          task:quality_gate: 0 5000
          task:publish: 0 10000
          task:alert: 0 15000
          task:refresh_dashboard: 0 20000
          cluster:ingest_cluster: 0 25000

        """;

    /// <summary>A window around one row of the column above, in the module's own units.</summary>
    private static DiagramViewport Around(double y) => new(-100, y - 100, 300, y + 100);

    private static IReadOnlyList<string> RemovedBy(IReadOnlyList<DiagramDelta> deltas) =>
        deltas.OfType<DiagramRemoveDelta>().SelectMany(delta => delta.ElementIds).ToList();

    private static IReadOnlyList<string> AddedBy(IReadOnlyList<DiagramDelta> deltas) =>
        deltas.OfType<DiagramAddDelta>().SelectMany(delta => delta.Elements).Select(element => element.Id).ToList();

    [Fact]
    public async Task AViewChange_BringsInWhatCameIntoView_AndTakesOutWhatLeft()
    {
        // Arrange: the reader opens the whole job, then settles on the first task.
        var body = CopyFixture("job.yml");
        await using var session = Open("databricks/job", body, WriteRegistration("databricks/job", "job.yml", SpreadOut));
        _ = session.Baseline();
        _ = session.UpdateView(Around(0));

        // Act: and then pans down to the third.
        var deltas = session.UpdateView(Around(10000));

        // Assert. This is the behavioural test Requirement 1.3 asks for, and the reason it is
        // written against the session rather than the client: it fails against a session that
        // answers `[]`, which is what this module did before it adopted the loop, and no
        // assertion that the client called UpdateView would have noticed.
        Assert.Contains("task:publish", AddedBy(deltas));
        Assert.Contains("task:ingest", RemovedBy(deltas));

        // Add before Remove, which is what this module's Diff emits - Requirement 4.3 guesses
        // the opposite order and the code is what counts.
        Assert.IsType<DiagramAddDelta>(deltas[0]);
        Assert.IsType<DiagramRemoveDelta>(deltas[1]);
    }

    [Fact]
    public async Task AnEdgeWithOneEndInView_BringsItsFarEndWithIt()
    {
        // Arrange.
        var body = CopyFixture("job.yml");
        await using var session = Open("databricks/job", body, WriteRegistration("databricks/job", "job.yml", SpreadOut));
        _ = session.Baseline();

        // Act: a window holding only `ingest` - `quality_gate` is 5000 units below it.
        var removed = RemovedBy(session.UpdateView(Around(0)));

        // Assert: the far end of `ingest -> quality_gate` stays, because a connector with
        // nothing to land on is worse than one element too many. One hop and no further, so the
        // task beyond it goes.
        Assert.DoesNotContain("task:quality_gate", removed);
        Assert.DoesNotContain("edge:ingest->quality_gate", removed);
        Assert.Contains("task:publish", removed);
    }

    [Fact]
    public async Task AViewportThatBringsNothingNew_AnswersWithNothing()
    {
        // Arrange.
        var body = CopyFixture("job.yml");
        await using var session = Open("databricks/job", body, WriteRegistration("databricks/job", "job.yml", SpreadOut));
        _ = session.Baseline();
        _ = session.UpdateView(Around(0));

        // Act: the reader nudges the view without uncovering anything.
        var deltas = session.UpdateView(Around(10));

        // Assert: a reader who is not going anywhere costs the connection nothing. This one
        // passes against a session returning `[]` too - it is the companion to the test above,
        // not the guard.
        Assert.Empty(deltas);
    }

    [Fact]
    public async Task AConnectionThatNeverReportsAViewport_StillHoldsTheWholeDiagram()
    {
        // Arrange & act.
        var body = CopyFixture("job.yml");
        await using var session = Open("databricks/job", body, WriteRegistration("databricks/job", "job.yml", SpreadOut));

        // Assert: the viewport starts unbounded, so adopting the loop changed nothing for a
        // client that has not reported yet - including the far end of the column.
        var elements = ElementsOf(session).Select(element => element.Id).ToList();
        Assert.Contains("task:ingest", elements);
        Assert.Contains("cluster:ingest_cluster", elements);
    }

    [Fact]
    public async Task AnEditOutsideTheViewport_IsNotPushedToAConnectionThatCannotSeeIt()
    {
        // Arrange.
        // Two tasks and no dependency between them, so the one-hop edge rule is not what this
        // test is measuring: `far` is out of view on its own account.
        var body = IoPath.Combine(_root, "edit.yml");
        await File.WriteAllTextAsync(body, "resources:\r\n  jobs:\r\n    j:\r\n      name: J\r\n      tasks:\r\n"
            + "        - task_key: near\r\n          notebook_task:\r\n            notebook_path: notebooks/near\r\n"
            + "        - task_key: far\r\n          notebook_task:\r\n            notebook_path: notebooks/far\r\n", TestContext.Current.CancellationToken);
        var adp = WriteRegistration("databricks/job", "edit.yml", "layout:\r\n  task:near: 0 0\r\n  task:far: 0 5000\r\n");

        await using var session = Open("databricks/job", body, adp);
        _ = session.Baseline();
        _ = session.UpdateView(Around(0));

        var pushed = new List<DiagramDelta>();
        session.Changed += (_, args) => pushed.AddRange(args.Deltas);

        // Act: somebody edits the task the reader has panned away from.
        await File.WriteAllTextAsync(body, "resources:\r\n  jobs:\r\n    j:\r\n      name: J\r\n      tasks:\r\n"
            + "        - task_key: near\r\n          notebook_task:\r\n            notebook_path: notebooks/near\r\n"
            + "        - task_key: far\r\n          notebook_task:\r\n            notebook_path: notebooks/far-edited\r\n", TestContext.Current.CancellationToken);
        _provider.GetRequiredService<IDatabricksDocumentStore>().Reload(body);

        // Assert.
        // The change path and the viewport path have to agree about what this connection holds.
        // They are written as two methods and were never exercised together, so nothing else in
        // this suite would notice them disagreeing: a change path that re-rendered unfiltered
        // would push `far` as an add to a client that was told to remove it moments earlier, and
        // the diagram would grow back the elements the viewport culled on the next save. Found on
        // timeline first (Agent 2), and the same shape here.
        Assert.DoesNotContain("task:far", AddedBy(pushed));
    }
}
