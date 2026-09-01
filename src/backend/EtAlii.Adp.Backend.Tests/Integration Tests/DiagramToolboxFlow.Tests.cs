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
/// The Toolbox panel renders whatever <c>DescribeToolbox</c> answers, so this proves the
/// answer over the real host: a registered mindmap yields the module's palette, and the
/// entry's drop action id names the same add-child action the menu and the keyboard run -
/// one implementation behind every trigger (tech.md's "Specifying a diagram type").
/// </summary>
public class DiagramToolboxFlowTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private const string DeveloperUsername = "admin";
    private const string DeveloperCredential = "changeme";
    private const string SessionTokenHeader = "session-token";

    private readonly WebApplicationFactory<Program> _factory;
    private readonly string _appDataRoot;
    private readonly string _projectFolder;

    public DiagramToolboxFlowTests(WebApplicationFactory<Program> baseFactory)
    {
        _appDataRoot = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.IntegrationTests", Guid.NewGuid().ToString("N"));
        _projectFolder = IoPath.Combine(_appDataRoot, "sample-project");
        Directory.CreateDirectory(_projectFolder);

        // A registered mindmap, and a plain file no diagram type claims.
        File.WriteAllText(IoPath.Combine(_projectFolder, "roadmap.adp"), "freeplane/mindmap\n");
        File.WriteAllText(
            IoPath.Combine(_projectFolder, "roadmap.mm"),
            "<map version=\"freeplane 1.11.5\">\n<node TEXT=\"roadmap\" ID=\"ID_1\"/>\n</map>\n");
        File.WriteAllText(IoPath.Combine(_projectFolder, "notes.txt"), "not a diagram\n");

        _factory = baseFactory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("developer");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IProjectStore>();
                services.AddSingleton<IProjectStore>(new FileProjectStore(_appDataRoot));
                // The problem cache must live and die with this test, not in the real user
                // profile the host's AddProblems registration points at (found by the
                // errors-and-warnings-panel manual pass: every run left a cache file behind).
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
        TestFolder.TryDelete(_appDataRoot);
    }

    [Fact]
    public async Task AMindmapAnswersWithTheModulesPalette_AndItsDropRunsTheAddChildAction()
    {
        // Arrange.
        using var channel = CreateChannel();
        var diagramClient = new DiagramService.DiagramServiceClient(channel);
        var headers = await LoginAsync(channel);
        var projectId = await AddProjectAsync(channel, headers);

        // Act.
        var response = await diagramClient.DescribeToolboxAsync(
            new DescribeToolboxRequest { ProjectId = projectId, Path = PathOf("roadmap.adp") },
            headers, cancellationToken: TestContext.Current.CancellationToken);

        // Assert.
        var item = Assert.Single(response.Items);
        Assert.Equal("Node", item.Label);
        Assert.Equal("mindmap.add-child", item.DropActionId);
        Assert.NotEmpty(item.Icon);
        Assert.NotEmpty(item.Description);
    }

    [Fact]
    public async Task AFileNoDiagramTypeClaims_AnswersWithAnEmptyPalette()
    {
        // Arrange.
        using var channel = CreateChannel();
        var diagramClient = new DiagramService.DiagramServiceClient(channel);
        var headers = await LoginAsync(channel);
        var projectId = await AddProjectAsync(channel, headers);

        // Act: unroutable and non-existent both answer the same, non-revealing way.
        var unroutable = await diagramClient.DescribeToolboxAsync(
            new DescribeToolboxRequest { ProjectId = projectId, Path = PathOf("notes.txt") },
            headers, cancellationToken: TestContext.Current.CancellationToken);
        var missing = await diagramClient.DescribeToolboxAsync(
            new DescribeToolboxRequest { ProjectId = projectId, Path = PathOf("gone.adp") },
            headers, cancellationToken: TestContext.Current.CancellationToken);

        // Assert.
        Assert.Empty(unroutable.Items);
        Assert.Empty(missing.Items);
    }

    private static Path PathOf(string fileName)
    {
        var path = new Path();
        path.Segments.Add(fileName);
        return path;
    }

    private GrpcChannel CreateChannel()
    {
        var httpClient = _factory.CreateDefaultClient();
        return GrpcChannel.ForAddress(httpClient.BaseAddress!, new GrpcChannelOptions { HttpClient = httpClient });
    }

    private static async Task<Metadata> LoginAsync(GrpcChannel channel)
    {
        var authClient = new AuthenticationService.AuthenticationServiceClient(channel);
        var response = await authClient.LoginAsync(new LoginRequest { Username = DeveloperUsername, Credential = DeveloperCredential }, cancellationToken: TestContext.Current.CancellationToken);
        return new Metadata { { SessionTokenHeader, response.Session.Value } };
    }

    private async Task<ShortGuid> AddProjectAsync(GrpcChannel channel, Metadata headers)
    {
        var projectClient = new ProjectService.ProjectServiceClient(channel);
        var pathMessage = new Path();
        pathMessage.Segments.AddRange(_projectFolder.Split(IoPath.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries));
        var response = await projectClient.AddProjectAsync(new AddProjectRequest { Path = pathMessage }, headers, cancellationToken: TestContext.Current.CancellationToken);
        return response.Added.Id;
    }
}
