using EtAlii.Adp.Authentication.Wire;
using EtAlii.Adp.Designer;
using EtAlii.Adp.Designer.TableModel;
using EtAlii.Adp.Diagram;
using EtAlii.Adp.Diagram.Wire;
using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.Projects;
using EtAlii.Adp.Projects.Wire;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;
using DesignerService = EtAlii.Adp.Designer.Wire.DesignerService;
using IoPath = System.IO.Path;
using Path = EtAlii.Adp.Documents.Wire.Path;
using ProjectService = EtAlii.Adp.Projects.Wire.ProjectService;
using Proto = EtAlii.Adp.Designer.Wire;
using WorkspaceService = EtAlii.Adp.Diagram.Wire.WorkspaceService;

namespace EtAlii.Adp.Backend.Tests;

/// <summary>
/// A table on the tab's one stream, against the real host (knowledge-designer task 10,
/// Requirements 10.2 and 10.7): a designer session's baseline and changes arriving on
/// <c>WorkspaceService.Watch</c> as its <c>table</c> member under the stream's id, the calls that
/// follow an open reaching that session, and the session ending with its stream.
/// </summary>
/// <remarks>
/// The session is a test double. What this covers is the carrying and the lifetimes, which are
/// the host's; what a designer's session does with a window or a gesture is its module's.
/// </remarks>
public sealed class TableStreamFlowTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private const string DeveloperUsername = "admin";
    private const string DeveloperCredential = "changeme";
    private const string SessionTokenHeader = "session-token";
    private static readonly TimeSpan MessageTimeout = TimeSpan.FromSeconds(10);

    private static readonly DesignerDefinition Sheet = new("fixture/sheet", "Fixture sheet", Formats: [new("YAML", ".yaml")]);

    /// <summary>Routed, and no module serves it.</summary>
    private static readonly DesignerDefinition Orphan = new("fixture/orphan", "Fixture orphan", Formats: [new("YAML", ".yaml")]);

    private readonly WebApplicationFactory<Program> _factory;
    private readonly string _appDataRoot;
    private readonly string _projectFolder;
    private readonly FakeSessionFactory _sessions = new();

    public TableStreamFlowTests(WebApplicationFactory<Program> baseFactory)
    {
        _appDataRoot = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.IntegrationTests", Guid.NewGuid().ToString("N"));
        _projectFolder = IoPath.Combine(_appDataRoot, "sample-project");
        Directory.CreateDirectory(_projectFolder);
        File.WriteAllText(IoPath.Combine(_projectFolder, "cities.adp"), "fixture/sheet\r\nbody: cities.yaml\r\n");
        File.WriteAllText(IoPath.Combine(_projectFolder, "cities.yaml"), "name: Cities\r\n");
        File.WriteAllText(IoPath.Combine(_projectFolder, "lost.adp"), "fixture/orphan\r\nbody: lost.yaml\r\n");
        File.WriteAllText(IoPath.Combine(_projectFolder, "lost.yaml"), "name: Lost\r\n");
        File.WriteAllText(IoPath.Combine(_projectFolder, "notes.txt"), "not a table\r\n");

        _factory = baseFactory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("developer");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IProjectStore>();
                services.AddSingleton<IProjectStore>(new FileProjectStore(_appDataRoot));
                services.RemoveAll<Problems.IProblemStore>();
                services.AddSingleton<Problems.IProblemStore>(provider => new Problems.ProblemStore(
                    _appDataRoot,
                    provider.GetRequiredService<DiagramFileRouter>(),
                    provider.GetRequiredService<DiagramValidators>()));

                // The test-double designers ride in beside whatever discovery found.
                services.RemoveAll<IDesignerDefinitionCatalog>();
                services.AddSingleton<IDesignerDefinitionCatalog>(provider => new DesignerDefinitionCatalog
                {
                    All = [.. provider.GetService<IReadOnlyList<DesignerDefinition>>() ?? [], Sheet, Orphan],
                });
                services.AddSingleton<IDesignerSessionFactory>(_sessions);
            });
        });
    }

    public void Dispose()
    {
        _factory.Dispose();
        TestFolder.TryDelete(_appDataRoot);
    }

    [Fact]
    public async Task ATablesBaselineAndChanges_ArriveOnWatch_UnderItsStreamId()
    {
        // Arrange.
        var session = await OpenSessionAsync();
        using var cts = CreateMessageTimeout();
        using var watch = Watch(session, TestContext.Current.CancellationToken);
        await ReadAsync(watch.ResponseStream, cts.Token);

        // Act: the table joins the stream through the unary call, under the client's id.
        Documents.Wire.ShortGuid streamId = ShortGuid.NewShortGuid();
        await session.Designer.OpenTableAsync(OpenRequest(session, streamId, "cities.adp"), session.Headers, cancellationToken: TestContext.Current.CancellationToken);

        // Assert: the baseline, as the table member and no other.
        var first = await ReadTableAsync(watch.ResponseStream, cts.Token);
        Assert.Equal(streamId, first.StreamId);
        Assert.Equal(Proto.TableStreamMessage.EventOneofCase.Baseline, first.EventCase);
        Assert.Equal("Cities", first.Baseline.Title);
        Assert.Equal(["Name", "Population", "Kind"], first.Baseline.Columns.Select(column => column.Name));
        Assert.Equal("number", first.Baseline.Columns[1].Kind);
        Assert.Equal(120, first.Baseline.Columns[1].Width);
        Assert.True(first.Baseline.Columns[0].IsTitle);
        Assert.Equal(["Capital (blue)", "Port ()"], first.Baseline.Columns[2].Options.Select(option => $"{option.Name} ({option.Color})"));
        Assert.Equal("single", first.Baseline.Columns[2].Settings["selection"]);
        Assert.Equal(["All cities"], first.Baseline.Views.Select(view => view.Name));
        Assert.Equal("v1", first.Baseline.Settings.ViewId);
        var sort = Assert.Single(first.Baseline.Settings.Sorts);
        Assert.Equal(("p2", true), (sort.ColumnId, sort.Descending));
        Assert.Equal("p3", first.Baseline.Settings.GroupBy);
        Assert.True(first.Baseline.Settings.Filter.Any);
        Assert.Equal("contains", first.Baseline.Settings.Filter.Items[0].Condition.Comparison);
        Assert.Equal(["dam"], first.Baseline.Settings.Filter.Items[0].Condition.Values);
        Assert.Equal("isEmpty", Assert.Single(first.Baseline.Settings.Filter.Items[1].Group.Items).Condition.Comparison);
        var finding = Assert.Single(first.Baseline.Findings);
        Assert.Equal(("fixture.note", "info", "r1", "p2"), (finding.Code, finding.Severity, finding.RowId, finding.ColumnId));
        Assert.Equal(2, first.Baseline.RowCount);

        // The session was opened for the registration and the body it names.
        var opened = Assert.Single(_sessions.Opened);
        Assert.Equal((ShortGuid)session.WatchId, opened.WatchId);
        Assert.Equal(_projectFolder, opened.RootPath);
        Assert.Equal(IoPath.Combine(_projectFolder, "cities.adp"), opened.RegistrationPath);
        Assert.Equal(IoPath.Combine(_projectFolder, "cities.yaml"), opened.BodyPath);

        // The window: the call reaches the session, and the rows it answers with arrive as a change.
        await session.Designer.SetTableWindowAsync(new Proto.SetTableWindowRequest { WatchId = session.WatchId, StreamId = streamId, First = 0, Count = 50 }, session.Headers, cancellationToken: TestContext.Current.CancellationToken);
        var rows = await ReadTableAsync(watch.ResponseStream, cts.Token);
        Assert.Equal(streamId, rows.StreamId);
        Assert.Equal(Proto.TableChange.ChangeOneofCase.Rows, rows.Change.ChangeCase);
        Assert.Equal(["r1", "r2"], rows.Change.Rows.Rows.Select(row => row.Id));
        Assert.Equal(["Amsterdam"], rows.Change.Rows.Rows[0].Cells[0].Values);
        Assert.Equal((0, 50), opened.Window);

        // The view.
        await session.Designer.SetTableViewAsync(new Proto.SetTableViewRequest { WatchId = session.WatchId, StreamId = streamId, ViewId = "v2" }, session.Headers, cancellationToken: TestContext.Current.CancellationToken);
        var structure = await ReadTableAsync(watch.ResponseStream, cts.Token);
        Assert.Equal(Proto.TableChange.ChangeOneofCase.Structure, structure.Change.ChangeCase);
        Assert.Equal("v2", structure.Change.Structure.Settings.ViewId);
        var findings = await ReadTableAsync(watch.ResponseStream, cts.Token);
        Assert.Equal(Proto.TableChange.ChangeOneofCase.Findings, findings.Change.ChangeCase);
        Assert.Equal(["error", "warning"], findings.Change.Findings.Findings.Select(found => found.Severity));

        // An edit: accepted at once, and its outcome arrives on the stream under the edit's id.
        Documents.Wire.ShortGuid editId = ShortGuid.NewShortGuid();
        var gesture = new Proto.TableGesture { Kind = "setCell", RowId = "r1", ColumnId = "p2", Index = 3, TargetId = "r2" };
        gesture.Values.Add("931298");
        gesture.Settings.Add("unit", "people");
        var accepted = await session.Designer.EditAsync(new Proto.TableEditRequest { WatchId = session.WatchId, StreamId = streamId, EditId = editId, Gesture = gesture }, session.Headers, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("", accepted.Error);
        var outcome = await ReadTableAsync(watch.ResponseStream, cts.Token);
        Assert.Equal(Proto.TableChange.ChangeOneofCase.Outcome, outcome.Change.ChangeCase);
        Assert.Equal(editId, outcome.Change.Outcome.EditId);
        Assert.True(outcome.Change.Outcome.Written);
        var taken = Assert.Single(opened.Gestures);
        Assert.Equal(("setCell", "r1", "p2", "", 3, "r2"), (taken.Kind, taken.RowId, taken.ColumnId, taken.ViewId, taken.Index, taken.TargetId));
        Assert.Equal(["931298"], taken.Values);
        Assert.Equal("people", taken.Settings["unit"]);

        // A gesture the session refuses is answered in the call, and nothing is pushed for it.
        var refused = await session.Designer.EditAsync(new Proto.TableEditRequest { WatchId = session.WatchId, StreamId = streamId, EditId = editId, Gesture = new Proto.TableGesture { Kind = "refuse" } }, session.Headers, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("This cannot be done.", refused.Error);

        // Closing the table disposes its session and leaves the connection open.
        await session.Designer.CloseTableAsync(new Proto.CloseTableRequest { WatchId = session.WatchId, StreamId = streamId }, session.Headers, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(await EventuallyAsync(() => opened.Disposed), "The session was not disposed when its stream was closed.");
        Assert.Equal(0, Streams().Count);
        Assert.NotNull(Connections().Find((ShortGuid)session.WatchId));
    }

    [Fact]
    public async Task EndingTheWatch_DisposesEveryTableSessionItCarried()
    {
        // Arrange.
        var session = await OpenSessionAsync();
        using var cts = CreateMessageTimeout();
        using var watchCancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var watch = Watch(session, watchCancellation.Token);
        await ReadAsync(watch.ResponseStream, cts.Token);
        await session.Designer.OpenTableAsync(OpenRequest(session, ShortGuid.NewShortGuid(), "cities.yaml"), session.Headers, cancellationToken: TestContext.Current.CancellationToken);
        await ReadTableAsync(watch.ResponseStream, cts.Token);
        var opened = Assert.Single(_sessions.Opened);
        Assert.False(opened.Disposed);

        // Act: the tab goes away.
        await watchCancellation.CancelAsync();
        watch.Dispose();

        // Assert: a session is per connection and dies with its stream.
        Assert.True(await EventuallyAsync(() => opened.Disposed), "A table session outlived the connection that carried it.");
        Assert.True(await EventuallyAsync(() => Streams().Count == 0), "A table stream was still registered after its connection ended.");
    }

    [Fact]
    public async Task ACallForATableThatIsNotOpen_IsTransient()
    {
        // Arrange.
        var session = await OpenSessionAsync();
        using var cts = CreateMessageTimeout();
        using var watch = Watch(session, TestContext.Current.CancellationToken);
        await ReadAsync(watch.ResponseStream, cts.Token);

        // Act.
        var refusal = await Assert.ThrowsAsync<RpcException>(async () => await session.Designer.SetTableWindowAsync(
            new Proto.SetTableWindowRequest { WatchId = session.WatchId, StreamId = ShortGuid.NewShortGuid(), Count = 10 },
            session.Headers,
            cancellationToken: TestContext.Current.CancellationToken));

        // Assert.
        Assert.Equal(StatusCode.Unavailable, refusal.StatusCode);
    }

    [Theory]
    [InlineData("notes.txt", StatusCode.FailedPrecondition)]
    [InlineData("missing.adp", StatusCode.FailedPrecondition)]
    [InlineData("lost.adp", StatusCode.Unimplemented)]
    public async Task OpenTable_ForWhatCannotBeOpened_IsRefusedWithAPermanentCode(string fileName, StatusCode expected)
    {
        // Arrange.
        var session = await OpenSessionAsync();
        using var cts = CreateMessageTimeout();
        using var watch = Watch(session, TestContext.Current.CancellationToken);
        await ReadAsync(watch.ResponseStream, cts.Token);

        // Act.
        var refusal = await Assert.ThrowsAsync<RpcException>(async () => await session.Designer.OpenTableAsync(
            OpenRequest(session, ShortGuid.NewShortGuid(), fileName), session.Headers, cancellationToken: TestContext.Current.CancellationToken));

        // Assert: the codes a diagram's open answers, so the client reads them the same way.
        Assert.Equal(expected, refusal.StatusCode);
        Assert.True(PermanentRefusal.IsPermanent(refusal.StatusCode));
        Assert.Empty(_sessions.Opened);
        Assert.Equal(0, Connections().Find((ShortGuid)session.WatchId)?.DiagramStreamCount);
    }

    [Fact]
    public async Task OpenTable_WithNoWatchOpen_IsTransient()
    {
        // Arrange.
        var session = await OpenSessionAsync();

        // Act.
        var refusal = await Assert.ThrowsAsync<RpcException>(async () => await session.Designer.OpenTableAsync(
            OpenRequest(session, ShortGuid.NewShortGuid(), "cities.adp"), session.Headers, cancellationToken: TestContext.Current.CancellationToken));

        // Assert.
        Assert.Equal(StatusCode.Unavailable, refusal.StatusCode);
        Assert.Empty(_sessions.Opened);
    }

    [Fact]
    public void TheDesignerService_HasNoStreamOfItsOwn()
    {
        // Assert: a table rides the tab's one stream. A server-streaming call here would be a
        // second stream per tab, which is what the workspace contract exists to prevent.
        var methods = DesignerService.Descriptor.Methods;
        Assert.NotEmpty(methods);
        Assert.Empty(methods.Where(method => method.IsServerStreaming || method.IsClientStreaming).Select(method => method.Name));
    }

    private WorkspaceConnections Connections() => _factory.Services.GetRequiredService<WorkspaceConnections>();

    private TableStreams Streams() => _factory.Services.GetRequiredService<TableStreams>();

    private static AsyncServerStreamingCall<WorkspaceMessage> Watch(Session session, CancellationToken cancellationToken) =>
        session.Workspace.Watch(new WatchWorkspaceRequest { ProjectId = session.ProjectId, WatchId = session.WatchId }, session.Headers, cancellationToken: cancellationToken);

    private static Proto.OpenTableRequest OpenRequest(Session session, Documents.Wire.ShortGuid streamId, string fileName)
    {
        var path = new Path();
        path.Segments.Add(fileName);
        return new Proto.OpenTableRequest { ProjectId = session.ProjectId, WatchId = session.WatchId, StreamId = streamId, Path = path };
    }

    private static CancellationTokenSource CreateMessageTimeout()
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(MessageTimeout);
        return cts;
    }

    private static async Task<WorkspaceMessage> ReadAsync(IAsyncStreamReader<WorkspaceMessage> stream, CancellationToken cancellationToken)
    {
        Assert.True(await stream.MoveNext(cancellationToken), "The workspace stream ended.");
        return stream.Current;
    }

    private static async Task<Proto.TableStreamMessage> ReadTableAsync(IAsyncStreamReader<WorkspaceMessage> stream, CancellationToken cancellationToken)
    {
        while (true)
        {
            var message = await ReadAsync(stream, cancellationToken);
            if (message.MessageCase == WorkspaceMessage.MessageOneofCase.Table)
            {
                return message.Table;
            }
        }
    }

    private static async Task<bool> EventuallyAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + MessageTimeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(25, TestContext.Current.CancellationToken);
        }

        return condition();
    }

    private async Task<Session> OpenSessionAsync()
    {
        var httpClient = _factory.CreateDefaultClient();
        var channel = GrpcChannel.ForAddress(httpClient.BaseAddress!, new GrpcChannelOptions { HttpClient = httpClient });
        var authClient = new AuthenticationService.AuthenticationServiceClient(channel);
        var login = await authClient.LoginAsync(new LoginRequest { Username = DeveloperUsername, Credential = DeveloperCredential }, cancellationToken: TestContext.Current.CancellationToken);
        var headers = new Metadata { { SessionTokenHeader, login.Session.Value } };

        var pathMessage = new Path();
        pathMessage.Segments.AddRange(_projectFolder.Split(IoPath.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries));
        var added = await new ProjectService.ProjectServiceClient(channel).AddProjectAsync(new AddProjectRequest { Path = pathMessage }, headers, cancellationToken: TestContext.Current.CancellationToken);

        return new Session(
            headers,
            added.Added.Id,
            ShortGuid.NewShortGuid(),
            new WorkspaceService.WorkspaceServiceClient(channel),
            new DesignerService.DesignerServiceClient(channel));
    }

    private sealed record Session(
        Metadata Headers,
        Documents.Wire.ShortGuid ProjectId,
        Documents.Wire.ShortGuid WatchId,
        WorkspaceService.WorkspaceServiceClient Workspace,
        DesignerService.DesignerServiceClient Designer);

    private sealed class FakeSessionFactory : IDesignerSessionFactory
    {
        public List<FakeSession> Opened { get; } = [];

        public string Origin => Sheet.Origin;

        public IDesignerSession Open(ShortGuid watchId, string rootPath, string registrationPath, string bodyPath)
        {
            var session = new FakeSession(watchId, rootPath, registrationPath, bodyPath);
            Opened.Add(session);
            return session;
        }
    }

    /// <summary>A two-row table that answers each call with one change, at once.</summary>
    private sealed class FakeSession(ShortGuid watchId, string rootPath, string registrationPath, string bodyPath) : IDesignerSession
    {
        private static readonly TableColumn[] Columns =
        [
            new("p1", "Name", "text", IsTitle: true),
            new("p2", "Population", "number", Width: 120),
            new("p3", "Kind", "select", [new TableOption("o1", "Capital", "blue"), new TableOption("o2", "Port")], Settings: new Dictionary<string, string> { ["selection"] = "single" }),
        ];

        private static readonly TableViewSettings Settings = new(
            "v1",
            [new TableSort("p2", Descending: true)],
            new TableFilterGroup(Any: true, [new TableCondition("p1", "contains", ["dam"]), new TableFilterGroup(Any: false, [new TableCondition("p2", "isEmpty")])]),
            GroupBy: "p3");

        private static readonly TableView[] Views = [new("v1", "All cities")];

        public ShortGuid WatchId { get; } = watchId;

        public string RootPath { get; } = rootPath;

        public string RegistrationPath { get; } = registrationPath;

        public string BodyPath { get; } = bodyPath;

        public (int First, int Count) Window { get; private set; }

        public List<TableGesture> Gestures { get; } = [];

        public bool Disposed { get; private set; }

        public event EventHandler<TableChangedEventArgs>? Changed;

        public TableBaseline Baseline() =>
            new("Cities", Columns, Views, Settings, 2, [new TableFinding("fixture.note", TableFindingSeverity.Info, "A note.", "r1", "p2")]);

        public void SetWindow(int first, int count)
        {
            Window = (first, count);
            Raise(new TableRowsChanged(0, [new TableRow("r1", Cells: [new TableCell("p1", ["Amsterdam"])]), new TableRow("r2")], 2));
        }

        public void SetActiveView(string viewId) => Changed?.Invoke(this, new TableChangedEventArgs(
        [
            new TableStructureChanged("Cities", Columns, Views, new TableViewSettings(viewId), 2),
            new TableFindingsChanged([new TableFinding("fixture.broken", TableFindingSeverity.Error, "Broken."), new TableFinding("fixture.odd", TableFindingSeverity.Warning, "Odd.")]),
        ]));

        public string Edit(ShortGuid editId, TableGesture gesture)
        {
            if (gesture.Kind == "refuse")
            {
                return "This cannot be done.";
            }

            Gestures.Add(gesture);
            Raise(new TableEditSettled(editId, Written: true));
            return "";
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }

        private void Raise(TableChange change) => Changed?.Invoke(this, new TableChangedEventArgs([change]));
    }
}
