using EtAlii.Adp.Context;
using EtAlii.Adp.Context.Wire;
using EtAlii.Adp.Diagram.Databricks;
using EtAlii.Adp.Diagram.Wire;
using EtAlii.Adp.Documents.Wire;
using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.TestSupport.Host;
using Xunit;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Backend.Tests;

/// <summary>
/// The Databricks arc over the real host: one configuration file serving three diagram types,
/// each opening and streaming its own reading; a reposition landing in the <c>.adp</c>'s
/// <c>layout:</c> block while the body never changes by a byte; and edits and undo travelling
/// the whole way (databricks-diagrams Requirements 1, 7 and the integration halves of 2 and 12).
/// </summary>
/// <remarks>
/// The halves worth proving here rather than in the module's own tests: that three
/// registrations onto ONE body each stream a different projection, that the <c>resource:</c>
/// header selects over the wire, and that layout persistence holds end to end - move, reopen,
/// position survives, undo returns the registration byte for byte with the body untouched
/// throughout.
/// </remarks>
public class DatabricksFlowTests(HostFixture host) : IClassFixture<HostFixture>
{
    private const string Lakehouse = """
        bundle:
          name: lakehouse

        resources:
          jobs:
            nightly:
              name: Nightly
              tasks:
                - task_key: ingest
                  notebook_task:
                    notebook_path: notebooks/ingest
                - task_key: transform
                  depends_on:
                    - task_key: ingest
                  notebook_task:
                    notebook_path: notebooks/transform
          pipelines:
            bronze:
              name: Bronze
              catalog: lakehouse_dev
              schema: bronze
              libraries:
                - notebook:
                    path: transformations/bronze

        targets:
          dev:
            mode: development
            default: true
        """;

    private readonly string _projectFolder = Seed(host.NewProjectFolder());

    private static string Seed(string projectFolder)
    {
        // One body, three registrations - the family's whole routing story in one folder.
        File.WriteAllText(IoPath.Combine(projectFolder, "lakehouse.yml"), Lakehouse);
        File.WriteAllText(IoPath.Combine(projectFolder, "lakehouse.adp"), "databricks/bundle\nbody: lakehouse.yml\n");
        File.WriteAllText(IoPath.Combine(projectFolder, "lakehouse.job.adp"), "databricks/job\nbody: lakehouse.yml\n");
        File.WriteAllText(
            IoPath.Combine(projectFolder, "lakehouse.pipeline.adp"),
            "databricks/pipeline\nbody: lakehouse.yml\nresource: bronze\n");
        return projectFolder;
    }

    [Fact]
    public async Task OneBody_StreamsThreeReadings_OnePerRegistration()
    {
        // Arrange: Requirement 1.4 - three MIME types over one file, each registration opening
        // its own projection; the pipeline one selected by its resource: header (Requirement 1.3).
        using var session = await FlowSession.OpenAsync(host, _projectFolder, TestContext.Current.CancellationToken);

        // Act.
        var bundle = await DiagramFlow.BaselineAsync(session, "lakehouse.adp");
        var job = await DiagramFlow.BaselineAsync(session, "lakehouse.job.adp");
        var pipeline = await DiagramFlow.BaselineAsync(session, "lakehouse.pipeline.adp");

        // Assert.
        Assert.Contains(bundle, element => element.Id.Value == "bundle");
        Assert.Contains(bundle, element => element.Id.Value == "resource:jobs/nightly");
        Assert.Contains(bundle, element => element.Id.Value == "target:dev");
        Assert.Contains(job, element => element.Id.Value == "task:ingest" && element.Type == "databricks/job+task");
        Assert.Contains(job, element => element.Id.Value == "edge:ingest->transform");
        Assert.Contains(pipeline, element => element.Id.Value == "pipeline");
        Assert.Contains(pipeline, element => element.Id.Value == "library:transformations/bronze");
        Assert.Contains(pipeline, element => element.Id.Value == "flow:pipeline->target");
    }

    [Fact]
    public async Task AReposition_LandsInTheAdp_SurvivesReopen_AndUndoReturnsIt_WithTheBodyUntouchedThroughout()
    {
        // Arrange: the layout-persistence promise (Requirement 7), end to end. The diagram must
        // be open on the SAME connection, because core will not edit a document a caller is not
        // looking at.
        using var session = await FlowSession.OpenAsync(host, _projectFolder, TestContext.Current.CancellationToken);
        var client = new DiagramService.DiagramServiceClient(session.Channel);
        var watchId = ShortGuid.NewShortGuid();
        var bodyPath = IoPath.Combine(_projectFolder, "lakehouse.yml");
        var adpPath = IoPath.Combine(_projectFolder, "lakehouse.job.adp");
        var bodyBefore = await File.ReadAllBytesAsync(bodyPath, TestContext.Current.CancellationToken);
        var adpBefore = await File.ReadAllTextAsync(adpPath, TestContext.Current.CancellationToken);

        using var cts = FlowSession.CreateMessageTimeout(TestContext.Current.CancellationToken);
        using var call = client.Open(
            new OpenDiagramRequest { ProjectId = session.ProjectId, WatchId = watchId, Path = DiagramFlow.PathOf("lakehouse.job.adp") },
            session.Headers, cancellationToken: cts.Token);
        await call.ResponseStream.ReadAddAsync(cts.Token);

        // Act: drag the ingest task to an authored spot.
        var moved = await client.MoveElementAsync(
            new MoveElementRequest
            {
                ProjectId = session.ProjectId,
                WatchId = watchId,
                Path = DiagramFlow.PathOf("lakehouse.job.adp"),
                ElementId = "task:ingest",
                Position = new Point2D { X = 300, Y = 180 },
            },
            session.Headers, cancellationToken: TestContext.Current.CancellationToken);

        // Assert: the position is in the registration, and nowhere near the body (7.3, 7.7).
        Assert.Equal("", moved.Error);
        Assert.Contains("layout:", await File.ReadAllTextAsync(adpPath, TestContext.Current.CancellationToken), StringComparison.Ordinal);
        Assert.Equal(new RegistrationPosition(300, 180), RegistrationLayout.Read(adpPath)["task:ingest"]);
        Assert.Equal(bodyBefore, await File.ReadAllBytesAsync(bodyPath, TestContext.Current.CancellationToken));

        // Act, continued: a fresh open sees the stored position overlaid (7.4).
        var reopened = await DiagramFlow.BaselineAsync(session, "lakehouse.job.adp");
        var ingest = reopened.Single(element => element.Id.Value == "task:ingest");
        Assert.Equal((300d, 180d), (ingest.Position.X, ingest.Position.Y));

        // Act, continued: one undo returns the registration byte for byte (7.6).
        var undone = await DiagramFlow.ExecuteProjectActionAsync(session, watchId, HistoryContextActionProvider.UndoActionId);

        // Assert.
        Assert.True(undone.Accepted, undone.Error);
        Assert.Equal(adpBefore, await File.ReadAllTextAsync(adpPath, TestContext.Current.CancellationToken));
        Assert.Equal(bodyBefore, await File.ReadAllBytesAsync(bodyPath, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ARunIfEdit_TravelsTheSelectionChain_AndIsOneUndoAway()
    {
        // Arrange: the property path over the real selection chain - which also proves a
        // databricks element resolves while every other module's resolver is registered beside
        // this one (Requirement 10.5, 12's integration half).
        using var session = await FlowSession.OpenAsync(host, _projectFolder, TestContext.Current.CancellationToken);
        var contextClient = session.Context;
        var watchId = ShortGuid.NewShortGuid();
        var entries = await DiagramFlow.EntriesAsync(session, watchId);
        var bodyPath = IoPath.Combine(_projectFolder, "lakehouse.yml");
        var before = await File.ReadAllTextAsync(bodyPath, TestContext.Current.CancellationToken);

        var selected = await contextClient.SelectAsync(
            new SelectRequest
            {
                ProjectId = session.ProjectId,
                WatchId = watchId,
                Selection = DiagramFlow.ElementChain(entries["lakehouse.job.adp"], "task:transform"),
            },
            session.Headers, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Empty(selected.Error);

        // Act.
        var result = await contextClient.SetPropertyAsync(
            new SetPropertyRequest
            {
                ProjectId = session.ProjectId,
                WatchId = watchId,
                PropertyId = DatabricksContextPropertyProvider.RunIfProperty,
                Value = "ALL_DONE",
            },
            session.Headers, cancellationToken: TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(result.Accepted, result.Error);
        Assert.Contains("run_if: ALL_DONE", await File.ReadAllTextAsync(bodyPath, TestContext.Current.CancellationToken), StringComparison.Ordinal);

        // Act, continued: one undo returns the body byte for byte.
        var undone = await DiagramFlow.ExecuteProjectActionAsync(session, watchId, HistoryContextActionProvider.UndoActionId);

        // Assert.
        Assert.True(undone.Accepted, undone.Error);
        Assert.Equal(before, await File.ReadAllTextAsync(bodyPath, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task APipelineScalarEdit_ThroughItsOwnRegistration_ReachesTheSharedBody()
    {
        // Arrange: the same body edited through the pipeline registration - the shared-store
        // half of Requirement 1.4: three diagram types, one document, one history.
        using var session = await FlowSession.OpenAsync(host, _projectFolder, TestContext.Current.CancellationToken);
        var contextClient = session.Context;
        var watchId = ShortGuid.NewShortGuid();
        var entries = await DiagramFlow.EntriesAsync(session, watchId);
        var bodyPath = IoPath.Combine(_projectFolder, "lakehouse.yml");
        var before = await File.ReadAllTextAsync(bodyPath, TestContext.Current.CancellationToken);

        var selected = await contextClient.SelectAsync(
            new SelectRequest
            {
                ProjectId = session.ProjectId,
                WatchId = watchId,
                Selection = DiagramFlow.ElementChain(entries["lakehouse.pipeline.adp"], "pipeline"),
            },
            session.Headers, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Empty(selected.Error);

        // Act.
        var result = await contextClient.SetPropertyAsync(
            new SetPropertyRequest
            {
                ProjectId = session.ProjectId,
                WatchId = watchId,
                PropertyId = $"{DatabricksContextPropertyProvider.PipelineScalarPrefix}catalog",
                Value = "lakehouse_prod",
            },
            session.Headers, cancellationToken: TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(result.Accepted, result.Error);
        Assert.Contains("catalog: lakehouse_prod", await File.ReadAllTextAsync(bodyPath, TestContext.Current.CancellationToken), StringComparison.Ordinal);

        var undone = await DiagramFlow.ExecuteProjectActionAsync(session, watchId, HistoryContextActionProvider.UndoActionId);
        Assert.True(undone.Accepted, undone.Error);
        Assert.Equal(before, await File.ReadAllTextAsync(bodyPath, TestContext.Current.CancellationToken));
    }
}
