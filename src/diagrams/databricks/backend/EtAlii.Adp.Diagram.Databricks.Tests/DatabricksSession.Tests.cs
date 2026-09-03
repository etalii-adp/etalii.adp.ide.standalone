using EtAlii.Adp.Backend;
using EtAlii.Adp.Backend.Diagrams;
using EtAlii.Adp.Backend.Hierarchy;
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
        var bodyBytes = File.ReadAllBytes(body);
        var adp = WriteRegistration("databricks/job", "job.yml");
        var adpBefore = File.ReadAllText(adp);

        // Act.
        await using var session = Open("databricks/job", body, adp);
        var refusal = await session.MoveElementToAsync("task:ingest", 120, 240, TestContext.Current.CancellationToken);

        // Assert.
        Assert.Equal("", refusal);
        Assert.Equal(new RegistrationPosition(120, 240), RegistrationLayout.Read(adp)["task:ingest"]);
        // The body file never changes by a byte (Requirement 7.7).
        Assert.Equal(bodyBytes, File.ReadAllBytes(body));
        // And the drag is one undo away, returning the .adp byte for byte (Requirement 7.6).
        await _provider.GetRequiredService<IHistoryStackStore>().Get(_root)
            .UndoAsync(TestContext.Current.CancellationToken);
        Assert.Equal(adpBefore, File.ReadAllText(adp));
    }

    [Fact]
    public async Task TheResourceHeader_PicksTheDeclaration_TheSessionShows()
    {
        // Arrange.
        // A file declaring two jobs: the resource: header names the second.
        var path = IoPath.Combine(_root, "jobs.yml");
        File.WriteAllText(path,
            "resources:\r\n  jobs:\r\n"
            + "    first:\r\n      name: First\r\n      tasks:\r\n        - task_key: a\r\n"
            + "    second:\r\n      name: Second\r\n      tasks:\r\n        - task_key: b\r\n");
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
}
