using EtAlii.Adp.Backend.Hierarchy;
using EtAlii.Adp.Backend.Projects;

using Grpc.Core;
using Grpc.Net.Client;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Xunit;

using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here
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

    public AnsibleStructureFlowTests(WebApplicationFactory<Program> baseFactory)
    {
        _appDataRoot = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.IntegrationTests", Guid.NewGuid().ToString("N"));
        _projectFolder = IoPath.Combine(_appDataRoot, "sample-project");
        _infrastructure = IoPath.Combine(_projectFolder, "infrastructure");
        Directory.CreateDirectory(_projectFolder);

        // A real Ansible project inside the project folder, registered the way Add would.
        AnsibleFixture.CopyTo(_infrastructure);
        File.WriteAllText(IoPath.Combine(_infrastructure, "infrastructure.adp"), "ansible/structure\n");

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
                    provider.GetRequiredService<Diagram.DiagramValidators>()));
            });
        });
    }

    public void Dispose()
    {
        _factory.Dispose();
        if (Directory.Exists(_appDataRoot))
        {
            Directory.Delete(_appDataRoot, recursive: true);
        }
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
        DiagramService.DiagramServiceClient client, Metadata headers, Contracts.ShortGuid projectId)
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

    private async Task<Contracts.ShortGuid> AddProjectAsync(GrpcChannel channel, Metadata headers)
    {
        var projectClient = new ProjectService.ProjectServiceClient(channel);
        var pathMessage = new Path();
        pathMessage.Segments.AddRange(_projectFolder.Split(IoPath.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries));
        var response = await projectClient.AddProjectAsync(
            new AddProjectRequest { Path = pathMessage }, headers, cancellationToken: TestContext.Current.CancellationToken);
        return response.Added.Id;
    }
}
