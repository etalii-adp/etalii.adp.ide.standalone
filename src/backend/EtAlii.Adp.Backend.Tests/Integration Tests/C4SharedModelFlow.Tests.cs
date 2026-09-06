using EtAlii.Adp.Authentication.Wire;
using EtAlii.Adp.Common;
using EtAlii.Adp.Common.Wire;
using EtAlii.Adp.Diagram.Wire;
using EtAlii.Adp.Projects;
using EtAlii.Adp.Projects.Wire;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here
using Path = EtAlii.Adp.Common.Wire.Path;
using ProjectService = EtAlii.Adp.Projects.Wire.ProjectService;

namespace EtAlii.Adp.Backend.Tests;

/// <summary>
/// C4's central premise, over the real host: two <c>.adp</c> files naming one <c>.dsl</c> are
/// two views of one model, not two drawings (c4-diagrams Requirements 1.2, 2.4, 2.6).
/// </summary>
public class C4SharedModelFlowTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private const string DeveloperUsername = "admin";
    private const string DeveloperCredential = "changeme";
    private const string SessionTokenHeader = "session-token";
    private static readonly TimeSpan MessageTimeout = TimeSpan.FromSeconds(10);

    private readonly WebApplicationFactory<Program> _factory;
    private readonly string _appDataRoot;
    private readonly string _projectFolder;

    private const string Model = """
        workspace "Bank" {
            model {
                u = person "Customer" "A customer."
                s = softwareSystem "Banking" "Does banking." {
                    web = container "Web" "Serves pages." "React"
                }
                u -> web "Uses" "HTTPS"
            }
            views {
                systemContext s "context" {
                    include *
                }
                container s "containers" {
                    include *
                }
            }
        }
        """;

    public C4SharedModelFlowTests(WebApplicationFactory<Program> baseFactory)
    {
        _appDataRoot = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.IntegrationTests", Guid.NewGuid().ToString("N"));
        _projectFolder = IoPath.Combine(_appDataRoot, "c4-project");
        Directory.CreateDirectory(_projectFolder);

        File.WriteAllText(IoPath.Combine(_projectFolder, "banking.dsl"), Model);
        // Two registrations, one body: the shape Requirement 2.4 exists for.
        File.WriteAllText(IoPath.Combine(_projectFolder, "context.adp"), "c4/context\nbody: banking.dsl\nview: context\n");
        File.WriteAllText(IoPath.Combine(_projectFolder, "containers.adp"), "c4/container\nbody: banking.dsl\nview: containers\n");

        _factory = baseFactory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("developer");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IProjectStore>();
                services.AddSingleton<IProjectStore>(new FileProjectStore(_appDataRoot));
                // The problem cache must live and die with this test, not in the real user
                // profile the host's AddProblems registration points at. A host booted without
                // this override leaves a cache file behind naming a temp folder that is deleted
                // moments later, and every later run then walks that dead root at startup: 605
                // such files had accumulated, costing the suite 22,591 warnings and an apparent
                // hang. DiagramToolboxFlowTests fixed this for itself; it never generalised.
                services.RemoveAll<Problems.IProblemStore>();
                services.AddSingleton<Problems.IProblemStore>(provider => new Problems.ProblemStore(
                    _appDataRoot,
                    provider.GetRequiredService<Hierarchy.DiagramFileRouter>(),
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
    public async Task TwoRegistrationsOverOneDocument_EachOpenTheirOwnView()
    {
        // Arrange.
        using var channel = CreateChannel();
        var headers = await LoginAsync(channel);
        var projectId = await AddProjectAsync(channel, headers);
        var client = new DiagramService.DiagramServiceClient(channel);

        // Arrange, continued.
        var context = await ElementIdsAsync(client, headers, projectId, "context.adp");
        var containers = await ElementIdsAsync(client, headers, projectId, "containers.adp");

        // Act.
        // A context view shows people and software systems; a container view shows the
        // containers inside its system. One document, two different answers.
        Assert.Contains("u", context);
        Assert.Contains("s", context);
        Assert.DoesNotContain("web", context);

        // Assert.
        Assert.Contains("web", containers);
        // The system in scope is the boundary on a container view, not a box.
        Assert.DoesNotContain("s", containers);
    }

    [Fact]
    public async Task ABareDslWithNoRegistration_OpensItsFirstDeclaredView()
    {
        // Arrange.
        // Requirement 2.6: a document dropped into the project on its own is still openable,
        // and shows the first view it declares.
        using var channel = CreateChannel();
        var headers = await LoginAsync(channel);
        var projectId = await AddProjectAsync(channel, headers);
        var client = new DiagramService.DiagramServiceClient(channel);

        // Act.
        var ids = await ElementIdsAsync(client, headers, projectId, "banking.dsl");

        // Assert.
        Assert.Contains("s", ids);
        Assert.DoesNotContain("web", ids);
    }

    [Fact]
    public async Task EveryViewCarriesItsOwnTitle_SoTwoTabsOfOneModelAreTellableApart()
    {
        // Arrange.
        using var channel = CreateChannel();
        var headers = await LoginAsync(channel);
        var projectId = await AddProjectAsync(channel, headers);
        var client = new DiagramService.DiagramServiceClient(channel);

        // Act.
        var context = await TitleOfAsync(client, headers, projectId, "context.adp");
        var containers = await TitleOfAsync(client, headers, projectId, "containers.adp");

        // Assert.
        Assert.Equal("System Context diagram for Banking", context);
        Assert.Equal("Container diagram for Banking", containers);
    }

    // Deleting one view while leaving the shared model alone is asserted in
    // DiagramFilePairTests.Delete_ARegistrationNamingASharedBody_LeavesTheBodyAlone, where the
    // ownership rule that guarantees it lives. Deletion reaches the backend through a context
    // action rather than a hierarchy RPC, so reproducing it here would exercise the action
    // plumbing rather than the property itself.

    // ---- helpers ----------------------------------------------------------------------

    /// <summary>The elements a diagram's baseline delivers, by id.</summary>
    private static async Task<IReadOnlyList<string>> ElementIdsAsync(
        DiagramService.DiagramServiceClient client,
        Metadata headers,
        Common.Wire.ShortGuid projectId,
        string fileName)
    {
        var elements = await BaselineAsync(client, headers, projectId, fileName);
        return elements.Select(element => element.Id.Value).ToArray();
    }

    /// <summary>The title the view's own element carries - what C4 requires every diagram to show.</summary>
    private static async Task<string> TitleOfAsync(
        DiagramService.DiagramServiceClient client,
        Metadata headers,
        Common.Wire.ShortGuid projectId,
        string fileName)
    {
        var elements = await BaselineAsync(client, headers, projectId, fileName);
        var view = elements.Single(element => element.Type == "c4/model+view");
        return Diagram.C4.C4ViewPayload.Parser.ParseFrom(view.Payload.Value.Span).Title;
    }

    /// <summary>Opens a diagram and reads the one add that carries its baseline.</summary>
    private static async Task<IReadOnlyList<Element>> BaselineAsync(
        DiagramService.DiagramServiceClient client,
        Metadata headers,
        Common.Wire.ShortGuid projectId,
        string fileName)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(MessageTimeout);

        var path = new Path();
        path.Segments.Add(fileName);
        using var call = client.Open(
            new OpenDiagramRequest { ProjectId = projectId, WatchId = ShortGuid.NewShortGuid(), Path = path },
            headers, cancellationToken: cts.Token);

        try
        {
            // The stream stays open after the baseline; one message is all this needs.
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
            new AddProjectRequest { Path = pathMessage }, headers,
            cancellationToken: TestContext.Current.CancellationToken);
        return response.Added.Id;
    }
}
