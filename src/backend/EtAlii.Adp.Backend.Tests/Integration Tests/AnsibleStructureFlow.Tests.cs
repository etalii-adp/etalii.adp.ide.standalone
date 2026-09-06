using EtAlii.Adp.Backend.Hierarchy;
using EtAlii.Adp.Backend.Projects;
using EtAlii.Adp.Common;
using EtAlii.Adp.Common.Wire;
using EtAlii.Adp.Diagram.Wire;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here
using Path = EtAlii.Adp.Common.Wire.Path;
using Wire = EtAlii.Adp.Diagram.AnsibleStructure.Wire;

namespace EtAlii.Adp.Backend.Tests;

/// <summary>
/// The Ansible structure diagram against the real host: a folder registered by one <c>.adp</c>,
/// opened, its baseline read, and the diagram following the folder as it changes.
/// </summary>
/// <remarks>
/// The point of doing this over gRPC rather than against the module's own classes is that this
/// is where the folder-subject novelty actually gets tested: core routes the registration,
/// resolves the session factory by origin, and never learns that the subject is a folder.
/// </remarks>
public class AnsibleStructureFlowTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private const string DeveloperUsername = "admin";
    private const string DeveloperCredential = "changeme";
    private const string SessionTokenHeader = "session-token";
    private static readonly TimeSpan MessageTimeout = TimeSpan.FromSeconds(15);

    private readonly WebApplicationFactory<Program> _factory;
    private readonly string _appDataRoot;
    private readonly string _projectFolder;
    private readonly string _infrastructure;

    public AnsibleStructureFlowTests(WebApplicationFactory<Program> factory)
    {
        _appDataRoot = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.IntegrationTests", Guid.NewGuid().ToString("N"));
        _projectFolder = IoPath.Combine(_appDataRoot, "sample-project");
        _infrastructure = IoPath.Combine(_projectFolder, "infrastructure");
        Directory.CreateDirectory(_projectFolder);

        // A real Ansible project inside the project folder, registered the way Add would.
        AnsibleFixture.CopyTo(_infrastructure);
        File.WriteAllText(IoPath.Combine(_infrastructure, "infrastructure.adp"), "ansible/structure\n");

        _factory = factory.WithWebHostBuilder(builder =>
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
                    provider.GetRequiredService<Common.DiagramValidators>()));
            });
        });
    }

    public void Dispose()
    {
        _factory.Dispose();
        TestFolder.TryDelete(_appDataRoot);
    }

    [Fact]
    public async Task ARegisteredFolder_OpensAndDeliversItsStructure()
    {
        // Arrange.
        using var channel = CreateChannel();
        var headers = await LoginAsync(channel);
        var projectId = await AddProjectAsync(channel, headers);
        var client = new DiagramService.DiagramServiceClient(channel);

        // Act.
        var elements = await BaselineAsync(client, headers, projectId);

        // Assert.
        // Core routed a registration whose body is itself, resolved this module's session
        // factory by origin, and never learned that the subject is a folder.
        var ids = elements.Select(element => element.Id.Value).ToArray();
        Assert.Contains("playbook:site.yml", ids);
        Assert.Contains("role:nginx", ids);
        Assert.Contains("inventory:inventories/production", ids);
        Assert.Contains(ids, id => id.StartsWith("edge:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ANodesPayload_CarriesWhatTheCanvasNeeds()
    {
        // Arrange.
        using var channel = CreateChannel();
        var headers = await LoginAsync(channel);
        var projectId = await AddProjectAsync(channel, headers);
        var client = new DiagramService.DiagramServiceClient(channel);

        // Act.
        var elements = await BaselineAsync(client, headers, projectId);
        var nginx = elements.Single(element => element.Id.Value == "role:nginx");
        var payload = Wire.AnsibleElementPayload.Parser.ParseFrom(nginx.Payload.Value.Span);

        // Assert.
        Assert.Equal("nginx", payload.Name);
        // The path the canvas hands to revealPath - project-relative, never absolute.
        Assert.Equal(["roles", "nginx"], payload.ProjectRelativePath);
        Assert.True(payload.Width > 0);
        Assert.Equal(2, payload.Contents.TaskFiles);
    }

    [Fact]
    public async Task AChangeInTheFolder_ReachesAnOpenDiagram()
    {
        // Arrange.
        using var channel = CreateChannel();
        var headers = await LoginAsync(channel);
        var projectId = await AddProjectAsync(channel, headers);
        var client = new DiagramService.DiagramServiceClient(channel);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(MessageTimeout);

        var path = new Path();
        path.Segments.Add("infrastructure");
        path.Segments.Add("infrastructure.adp");
        using var call = client.Open(
            new OpenDiagramRequest { ProjectId = projectId, WatchId = ShortGuid.NewShortGuid(), Path = path },
            headers, cancellationToken: cts.Token);

        // The baseline first, so the change that follows is the one being observed.
        Assert.True(await call.ResponseStream.MoveNext(cts.Token));

        // Act.
        // A brand-new role appears in the folder. Nothing about the .adp changed.
        var added = IoPath.Combine(_infrastructure, "roles", "redis", "tasks");
        Directory.CreateDirectory(added);
        await File.WriteAllTextAsync(
            IoPath.Combine(added, "main.yml"),
            "---\n- name: Install redis\n  ansible.builtin.package:\n    name: redis\n",
            cts.Token);

        // Assert.
        // Requirement 3.5: the diagram a reader leaves open stays true.
        var arrived = await WaitForElementAsync(call.ResponseStream, "role:redis", cts.Token);
        Assert.True(arrived, "The new role never reached the open diagram.");
    }

    [Fact]
    public async Task OpeningAndReadingADiagram_ChangesNothingInTheFolder()
    {
        // Arrange.
        // The end-to-end form of Requirement 1.1: not just the module's own components, but the
        // whole host serving a real gRPC open.
        using var channel = CreateChannel();
        var headers = await LoginAsync(channel);
        var projectId = await AddProjectAsync(channel, headers);
        var client = new DiagramService.DiagramServiceClient(channel);
        var before = Snapshot();

        // Act.
        await BaselineAsync(client, headers, projectId);

        // Assert.
        Assert.Equal(before.Keys.Order(StringComparer.Ordinal), Snapshot().Keys.Order(StringComparer.Ordinal));
        foreach (var (path, bytes) in before)
        {
            var bytesToCheck = await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken);
            Assert.True(bytes.SequenceEqual(bytesToCheck), $"{path} changed.");
        }
    }


    [Fact]
    public async Task ARepositionOverTheWire_LandsInTheRegistration_ReopensThere_UndoesBack_AndNeverTouchesAnsiblesFiles()
    {
        // Arrange.
        // The whole cycle helm proved for a chart, for a folder of Ansible: the drag goes over
        // gRPC, the position lands in the .adp, the reopened diagram shows it, undo puts the
        // registration's bytes back, and nothing Ansible owns is written at any point
        // (Requirements 2.2, 2.3, 2.4, 2.5).
        using var channel = CreateChannel();
        var headers = await LoginAsync(channel);
        var projectId = await AddProjectAsync(channel, headers);
        var client = new DiagramService.DiagramServiceClient(channel);

        var registration = IoPath.Combine(_infrastructure, "infrastructure.adp");
        var registrationBefore = await File.ReadAllBytesAsync(registration, TestContext.Current.CancellationToken);
        var ansibleFilesBefore = Snapshot()
            .Where(entry => !string.Equals(entry.Key, registration, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.OrdinalIgnoreCase);

        var path = new Path();
        path.Segments.Add("infrastructure");
        path.Segments.Add("infrastructure.adp");
        var watchId = ShortGuid.NewShortGuid();

        // The diagram must be open for the move to reach a session: the unary leg finds it
        // through the viewport registry the stream registered.
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(MessageTimeout);
        using var call = client.Open(
            new OpenDiagramRequest { ProjectId = projectId, WatchId = watchId, Path = path },
            headers, cancellationToken: cts.Token);
        Assert.True(await call.ResponseStream.MoveNext(cts.Token));

        // Act.
        var moved = await client.MoveElementAsync(
            new MoveElementRequest
            {
                ProjectId = projectId,
                WatchId = watchId,
                Path = path,
                ElementId = "role:nginx",
                Position = new Point2D { X = 321, Y = 123 },
            },
            headers,
            cancellationToken: cts.Token);

        // Assert 1: the write landed where the layout rule says it must, and nowhere else.
        Assert.Equal(string.Empty, moved.Error);
        Assert.Equal(
            new RegistrationPosition(321, 123),
            RegistrationLayout.Read(registration)["role:nginx"]);

        foreach (var (file, bytes) in ansibleFilesBefore)
        {
            var now = await File.ReadAllBytesAsync(file, TestContext.Current.CancellationToken);
            Assert.True(bytes.SequenceEqual(now), $"The reposition wrote {file}, which Ansible owns.");
        }

        // Assert 2: a diagram opened afresh draws the node where the user put it - the overlay
        // outlives the session, which is what makes the position authored rather than local.
        var reopened = await BaselineAsync(client, headers, projectId);
        var nginx = reopened.Single(element => element.Id.Value == "role:nginx");
        Assert.Equal(321, nginx.Position.X);
        Assert.Equal(123, nginx.Position.Y);

        // Assert 3: one undo, and the registration is byte-for-byte what it was.
        var history = _factory.Services.GetRequiredService<IHistoryStackStore>().Get(_projectFolder);
        await history.UndoAsync(TestContext.Current.CancellationToken);
        Assert.Equal(registrationBefore, await File.ReadAllBytesAsync(registration, TestContext.Current.CancellationToken));
    }

    // ---- helpers -------------------------------------------------------------------------

    private Dictionary<string, byte[]> Snapshot() =>
        Directory.GetFiles(_infrastructure, "*", SearchOption.AllDirectories)
            .ToDictionary(path => path, File.ReadAllBytes);

    private static async Task<bool> WaitForElementAsync(IAsyncStreamReader<Delta> stream, string elementId, CancellationToken cancellationToken)
    {
        try
        {
            while (await stream.MoveNext(cancellationToken))
            {
                if (stream.Current.ActionCase == Delta.ActionOneofCase.Add &&
                    stream.Current.Add.Elements.Any(element => element.Id.Value == elementId))
                {
                    return true;
                }
            }
        }
        catch (RpcException exception) when (exception.StatusCode == StatusCode.Cancelled)
        {
            // The deadline, which is this helper's way of giving up.
        }

        return false;
    }

    private async Task<IReadOnlyList<Element>> BaselineAsync(
        DiagramService.DiagramServiceClient client, Metadata headers, Common.Wire.ShortGuid projectId)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(MessageTimeout);

        var path = new Path();
        path.Segments.Add("infrastructure");
        path.Segments.Add("infrastructure.adp");
        using var call = client.Open(
            new OpenDiagramRequest { ProjectId = projectId, WatchId = ShortGuid.NewShortGuid(), Path = path },
            headers, cancellationToken: cts.Token);

        try
        {
            while (await call.ResponseStream.MoveNext(cts.Token))
            {
                if (call.ResponseStream.Current.ActionCase == Delta.ActionOneofCase.Add)
                {
                    return [.. call.ResponseStream.Current.Add.Elements];
                }
            }
        }
        catch (RpcException exception) when (exception.StatusCode == StatusCode.Cancelled)
        {
            // Reading stopped at the deadline, which is how this helper ends normally.
        }

        return [];
    }

    private GrpcChannel CreateChannel()
    {
        var httpClient = _factory.CreateDefaultClient();
        return GrpcChannel.ForAddress(httpClient.BaseAddress!, new GrpcChannelOptions { HttpClient = httpClient });
    }

    private static async Task<Metadata> LoginAsync(GrpcChannel channel)
    {
        var authClient = new AuthenticationService.AuthenticationServiceClient(channel);
        var response = await authClient.LoginAsync(
            new LoginRequest { Username = DeveloperUsername, Credential = DeveloperCredential },
            cancellationToken: TestContext.Current.CancellationToken);
        return new Metadata { { SessionTokenHeader, response.Session.Value } };
    }

    private async Task<Common.Wire.ShortGuid> AddProjectAsync(GrpcChannel channel, Metadata headers)
    {
        var projectClient = new ProjectService.ProjectServiceClient(channel);
        var pathMessage = new Path();
        pathMessage.Segments.AddRange(_projectFolder.Split(IoPath.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries));
        var response = await projectClient.AddProjectAsync(
            new AddProjectRequest { Path = pathMessage }, headers, cancellationToken: TestContext.Current.CancellationToken);
        return response.Added.Id;
    }
}
