using System.Text;
using EtAlii.Adp.Backend.Projects;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Backend.Tests;

/// <summary>
/// Diagrams-first resolution over the real host (modular-text-editors Requirements 5.1, 3.1,
/// 5.4). The regression this guards, specifically: a bare <c>.dsl</c> with NO <c>.adp</c>
/// registration anywhere still opens as a C4 diagram, because <c>.dsl</c> is a non-shared
/// extension the router claims on sight - the one behaviour a text-editor family most
/// plausibly breaks by being consulted first. The assertion tells the two outcomes apart by
/// what streams: a diagram baselines its own elements, an editor baselines exactly one
/// synthetic <c>"content"</c> element.
/// </summary>
public class EditorResolutionTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private const string DeveloperUsername = "admin";
    private const string DeveloperCredential = "changeme";
    private const string SessionTokenHeader = "session-token";

    private readonly string _appDataRoot;
    private readonly string _projectFolder;
    private readonly WebApplicationFactory<Program> _factory;

    public EditorResolutionTests(WebApplicationFactory<Program> baseFactory)
    {
        _appDataRoot = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.IntegrationTests", Guid.NewGuid().ToString("N"));
        _projectFolder = IoPath.Combine(_appDataRoot, "sample-project");
        Directory.CreateDirectory(_projectFolder);

        // A bare .dsl - no .adp beside it, deliberately: adding one would test the wrong
        // scenario - and a file no diagram type claims.
        File.WriteAllText(
            IoPath.Combine(_projectFolder, "design.dsl"),
            "workspace \"W\" {\n  model {\n    p = person \"P\"\n  }\n  views {\n    systemLandscape \"sl\" {\n      include *\n    }\n  }\n}\n");
        File.WriteAllText(IoPath.Combine(_projectFolder, "notes.txt"), "just some notes\n");

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
                    provider.GetRequiredService<Hierarchy.DiagramFileRouter>(),
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
    public async Task ABareUnregisteredDsl_StillOpensAsAC4Diagram()
    {
        // Arrange.
        using var channel = CreateChannel();
        var headers = await LoginAsync(channel);
        var projectId = await AddProjectAsync(channel, headers);
        var diagramClient = new DiagramService.DiagramServiceClient(channel);

        // Act.
        var firstAdd = await FirstAddDeltaAsync(diagramClient, headers, projectId, "design.dsl");

        // Assert: the baseline is the DIAGRAM's own elements - were resolution ever inverted,
        // the editor's one synthetic "content" element would arrive here instead.
        Assert.NotEmpty(firstAdd.Elements);
        Assert.DoesNotContain(firstAdd.Elements, element => element.Id?.Value == "content");
    }

    [Fact]
    public async Task AFileNoDiagramClaims_OpensInThePlainEditor()
    {
        // Arrange (Requirements 3.1, 5.4: no registration, no ceremony - the fallback answers).
        using var channel = CreateChannel();
        var headers = await LoginAsync(channel);
        var projectId = await AddProjectAsync(channel, headers);
        var diagramClient = new DiagramService.DiagramServiceClient(channel);

        // Act.
        var firstAdd = await FirstAddDeltaAsync(diagramClient, headers, projectId, "notes.txt");

        // Assert: one synthetic element carrying the whole file.
        var element = Assert.Single(firstAdd.Elements);
        Assert.Equal("content", element.Id?.Value);
        Assert.Equal("editor/plain", element.Type);
        Assert.Equal("just some notes\n", Encoding.UTF8.GetString(element.Payload!.Value.Span));
    }

    /// <summary>Opens the stream and returns the first Add delta the baseline produces.</summary>
    private static async Task<Add> FirstAddDeltaAsync(
        DiagramService.DiagramServiceClient diagramClient,
        Metadata headers,
        Contracts.ShortGuid projectId,
        string fileName)
    {
        var path = new Path();
        path.Segments.Add(fileName);
        using var call = diagramClient.Open(
            new OpenDiagramRequest { ProjectId = projectId, WatchId = ShortGuid.NewShortGuid(), Path = path },
            headers,
            deadline: DateTime.UtcNow.AddSeconds(30));

        while (await call.ResponseStream.MoveNext(TestContext.Current.CancellationToken))
        {
            if (call.ResponseStream.Current.ActionCase == Delta.ActionOneofCase.Add)
            {
                return call.ResponseStream.Current.Add;
            }
        }

        throw new InvalidOperationException($"The stream for '{fileName}' ended without an Add delta.");
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
            new AddProjectRequest { Path = pathMessage },
            headers,
            cancellationToken: TestContext.Current.CancellationToken);
        return response.Added.Id;
    }
}
