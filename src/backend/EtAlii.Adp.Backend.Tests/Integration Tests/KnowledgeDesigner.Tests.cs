using EtAlii.Adp.Authentication.Wire;
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
/// The Knowledge designer through the host, over gRPC (knowledge-designer Requirements 10.1, 8.1
/// and 5.5): the deployed module opens the shipped example in each of its three formats, shows it,
/// takes an edit and writes it, shows a change made on disk, and shows a two-way relation from
/// both of two open tables.
/// </summary>
/// <remarks>
/// Nothing here is a test double. The host is the one <c>Program.cs</c> builds, the module is the
/// one discovery found, and the files are copies of the examples the module ships.
/// </remarks>
public sealed class KnowledgeDesignerTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private const string DeveloperUsername = "admin";
    private const string DeveloperCredential = "changeme";
    private const string SessionTokenHeader = "session-token";
    private static readonly TimeSpan MessageTimeout = TimeSpan.FromSeconds(20);

    private readonly WebApplicationFactory<Program> _factory;
    private readonly string _appDataRoot;
    private readonly string _projectFolder;

    public KnowledgeDesignerTests(WebApplicationFactory<Program> baseFactory)
    {
        _appDataRoot = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.IntegrationTests", Guid.NewGuid().ToString("N"));
        _projectFolder = IoPath.Combine(_appDataRoot, "tables");
        Directory.CreateDirectory(_projectFolder);

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
            });
        });
    }

    public void Dispose()
    {
        _factory.Dispose();
        TestFolder.TryDelete(_appDataRoot);
    }

    /// <summary>The module's examples folder, found by walking up from the test binary.</summary>
    private static string Examples()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = IoPath.Combine(directory.FullName, "src", "designers", "knowledge", "examples");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("The Knowledge designer's examples were not found above the test binary.");
    }

    /// <summary>The example and the table it relates to in the project, each with its registration.</summary>
    private void Seed(string extension)
    {
        File.Copy(IoPath.Combine(Examples(), "cities" + extension), IoPath.Combine(_projectFolder, "cities" + extension));
        File.Copy(IoPath.Combine(Examples(), "provinces.yaml"), IoPath.Combine(_projectFolder, "provinces.yaml"));
        File.WriteAllText(IoPath.Combine(_projectFolder, "cities.adp"), $"etalii/knowledge\r\nbody: cities{extension}\r\n");
        File.WriteAllText(IoPath.Combine(_projectFolder, "provinces.adp"), "etalii/knowledge\r\nbody: provinces.yaml\r\n");
    }

    [Theory]
    [InlineData(".yaml")]
    [InlineData(".json")]
    [InlineData(".xml")]
    public async Task AKnowledgeFile_IsOpened_Shown_Edited_AndClosed(string extension)
    {
        // Arrange.
        Seed(extension);
        var body = IoPath.Combine(_projectFolder, "cities" + extension);
        var session = await OpenSessionAsync();
        using var cts = CreateMessageTimeout();
        using var watch = Watch(session, TestContext.Current.CancellationToken);
        await ReadAsync(watch.ResponseStream, cts.Token);

        // Act: opened through its registration.
        Documents.Wire.ShortGuid streamId = ShortGuid.NewShortGuid();
        await session.Designer.OpenTableAsync(OpenRequest(session, streamId, "cities.adp"), session.Headers, cancellationToken: TestContext.Current.CancellationToken);

        // Assert: the table, editable, with nothing to report - and the relation resolved against the other file.
        var first = await ReadTableAsync(watch.ResponseStream, streamId, cts.Token);
        Assert.Equal(Proto.TableStreamMessage.EventOneofCase.Baseline, first.EventCase);
        Assert.Equal("Cities", first.Baseline.Title);
        Assert.Equal("", first.Baseline.ReadOnlyReason);
        Assert.Empty(first.Baseline.Findings);
        Assert.Equal(["All cities", "Map"], first.Baseline.Views.Select(view => view.Name));
        var province = first.Baseline.Columns.Single(column => column.Name == "Province");
        Assert.Equal("relation", province.Kind);
        Assert.Equal(["Noord-Holland", "Zuid-Holland", "Antwerpen"], province.Options.Select(option => option.Name));

        // Act: the plain view, and a window of it.
        await session.Designer.SetTableViewAsync(new Proto.SetTableViewRequest { WatchId = session.WatchId, StreamId = streamId, ViewId = "v2" }, session.Headers, cancellationToken: TestContext.Current.CancellationToken);
        await session.Designer.SetTableWindowAsync(new Proto.SetTableWindowRequest { WatchId = session.WatchId, StreamId = streamId, First = 0, Count = 50 }, session.Headers, cancellationToken: TestContext.Current.CancellationToken);

        // Assert: both rows and the line a row is added at; the related row by its title.
        var rows = await UntilAsync(watch.ResponseStream, streamId, message => message.Change?.Rows is { } pushed && pushed.Rows.Select(row => row.Id).SequenceEqual(["r1", "r2", ""]), cts.Token);
        var related = rows.Change.Rows.Rows[0].Cells.Single(cell => cell.ColumnId == province.Id);
        Assert.Equal(["nh"], related.Values);
        Assert.Equal(["Noord-Holland"], related.Labels);

        // Act: an edit.
        Documents.Wire.ShortGuid editId = ShortGuid.NewShortGuid();
        var gesture = new Proto.TableGesture { Kind = "setCell", RowId = "r2", ColumnId = "p1" };
        gesture.Values.Add("Antwerpen");
        var accepted = await session.Designer.EditAsync(new Proto.TableEditRequest { WatchId = session.WatchId, StreamId = streamId, EditId = editId, Gesture = gesture }, session.Headers, cancellationToken: TestContext.Current.CancellationToken);

        // Assert: accepted at once, written behind, and in the file.
        Assert.Equal("", accepted.Error);
        var outcome = await UntilAsync(watch.ResponseStream, streamId, message => message.Change?.Outcome is { } settled && settled.EditId.Equals(editId), cts.Token);
        Assert.True(outcome.Change.Outcome.Written, outcome.Change.Outcome.Error);
        Assert.Contains("Antwerpen", await File.ReadAllTextAsync(body, TestContext.Current.CancellationToken), StringComparison.Ordinal);

        // Act: a refused edit is answered in the call.
        var refused = await session.Designer.EditAsync(
            new Proto.TableEditRequest { WatchId = session.WatchId, StreamId = streamId, EditId = ShortGuid.NewShortGuid(), Gesture = new Proto.TableGesture { Kind = "deleteColumn", ColumnId = "p1" } },
            session.Headers,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert.
        Assert.Equal("The title property is always there: it cannot be deleted or hidden.", refused.Error);

        // Act: another program changes the file.
        var text = await File.ReadAllTextAsync(body, TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(body, text.Replace("Amsterdam", "Mokum", StringComparison.Ordinal), TestContext.Current.CancellationToken);

        // Assert: it appears on the stream, without reopening.
        await UntilAsync(
            watch.ResponseStream,
            streamId,
            message => message.Change?.Rows is { } pushed && pushed.Rows.FirstOrDefault(row => row.Id == "r1")?.Cells.FirstOrDefault(cell => cell.ColumnId == "p1")?.Values.FirstOrDefault() == "Mokum",
            cts.Token);

        // Act and assert: closed, and the connection stays.
        await session.Designer.CloseTableAsync(new Proto.CloseTableRequest { WatchId = session.WatchId, StreamId = streamId }, session.Headers, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(_factory.Services.GetRequiredService<WorkspaceConnections>().Find((ShortGuid)session.WatchId));
    }

    [Fact]
    public async Task ATwoWayRelation_IsShownFromBothOfTwoOpenTables()
    {
        // Arrange: both tables open on one connection.
        Seed(".yaml");
        var session = await OpenSessionAsync();
        using var cts = CreateMessageTimeout();
        using var watch = Watch(session, TestContext.Current.CancellationToken);
        await ReadAsync(watch.ResponseStream, cts.Token);
        Documents.Wire.ShortGuid cities = ShortGuid.NewShortGuid();
        Documents.Wire.ShortGuid provinces = ShortGuid.NewShortGuid();
        await session.Designer.OpenTableAsync(OpenRequest(session, cities, "cities.adp"), session.Headers, cancellationToken: TestContext.Current.CancellationToken);
        await session.Designer.OpenTableAsync(OpenRequest(session, provinces, "provinces.adp"), session.Headers, cancellationToken: TestContext.Current.CancellationToken);
        await session.Designer.SetTableWindowAsync(new Proto.SetTableWindowRequest { WatchId = session.WatchId, StreamId = provinces, First = 0, Count = 50 }, session.Headers, cancellationToken: TestContext.Current.CancellationToken);

        // Act: a two-way relation made from the cities' table.
        var relate = new Proto.TableGesture { Kind = "addRelation" };
        relate.Values.Add("Near");
        relate.Settings.Add("target", "provinces.yaml");
        relate.Settings.Add("counterpart", "Cities nearby");
        var made = await session.Designer.EditAsync(new Proto.TableEditRequest { WatchId = session.WatchId, StreamId = cities, EditId = ShortGuid.NewShortGuid(), Gesture = relate }, session.Headers, cancellationToken: TestContext.Current.CancellationToken);

        // Assert: the other table gets its side, computed, without being reopened.
        Assert.Equal("", made.Error);
        var structure = await UntilAsync(watch.ResponseStream, provinces, message => message.Change?.Structure is { } pushed && pushed.Columns.Any(column => column.Name == "Cities nearby"), cts.Token);
        var nearby = structure.Change.Structure.Columns.Single(column => column.Name == "Cities nearby");
        Assert.Equal("true", nearby.Settings["computed"]);

        // Act: a value given on the side that holds it. The property's id is the one the cities' file was given.
        var nearId = await NearIdAsync();
        var set = new Proto.TableGesture { Kind = "setCell", RowId = "r1", ColumnId = nearId };
        set.Values.Add("zh");
        var accepted = await session.Designer.EditAsync(new Proto.TableEditRequest { WatchId = session.WatchId, StreamId = cities, EditId = ShortGuid.NewShortGuid(), Gesture = set }, session.Headers, cancellationToken: TestContext.Current.CancellationToken);

        // Assert: the province shows the city, by its title.
        Assert.Equal("", accepted.Error);
        var shown = await UntilAsync(
            watch.ResponseStream,
            provinces,
            message => message.Change?.Rows is { } pushed && pushed.Rows.FirstOrDefault(row => row.Id == "zh")?.Cells.Any(cell => cell.ColumnId == nearby.Id) == true,
            cts.Token);
        var cell = shown.Change.Rows.Rows.Single(row => row.Id == "zh").Cells.Single(candidate => candidate.ColumnId == nearby.Id);
        Assert.Equal(["r1"], cell.Values);
        Assert.Equal(["Amsterdam"], cell.Labels);

        // And it is not filled in from that side.
        var back = new Proto.TableGesture { Kind = "setCell", RowId = "an", ColumnId = nearby.Id };
        back.Values.Add("r2");
        var refused = await session.Designer.EditAsync(new Proto.TableEditRequest { WatchId = session.WatchId, StreamId = provinces, EditId = ShortGuid.NewShortGuid(), Gesture = back }, session.Headers, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("This side of the relation is filled in from the other table.", refused.Error);
    }

    /// <summary>The id the cities' file gave the relation named Near: the id on the line before its name.</summary>
    private async Task<string> NearIdAsync()
    {
        var lines = await File.ReadAllLinesAsync(IoPath.Combine(_projectFolder, "cities.yaml"), TestContext.Current.CancellationToken);
        var name = Array.FindIndex(lines, line => line.Trim() == "name: Near");
        Assert.True(name > 0, "The cities' file has no property named Near.");
        return lines[name - 1].Trim()["- id: ".Length..];
    }

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

    /// <summary>The next message of one table's stream.</summary>
    private static async Task<Proto.TableStreamMessage> ReadTableAsync(IAsyncStreamReader<WorkspaceMessage> stream, Documents.Wire.ShortGuid streamId, CancellationToken cancellationToken)
    {
        while (true)
        {
            var message = await ReadAsync(stream, cancellationToken);
            if (message.MessageCase == WorkspaceMessage.MessageOneofCase.Table && message.Table.StreamId.Equals(streamId))
            {
                return message.Table;
            }
        }
    }

    /// <summary>The first message of one table's stream that is what is waited for; whatever arrives before it is passed over.</summary>
    private static async Task<Proto.TableStreamMessage> UntilAsync(IAsyncStreamReader<WorkspaceMessage> stream, Documents.Wire.ShortGuid streamId, Func<Proto.TableStreamMessage, bool> wanted, CancellationToken cancellationToken)
    {
        while (true)
        {
            var message = await ReadTableAsync(stream, streamId, cancellationToken);
            if (wanted(message))
            {
                return message;
            }
        }
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
}
