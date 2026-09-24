using EtAlii.Adp.Authentication.Wire;
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
using IoPath = System.IO.Path;
using Path = EtAlii.Adp.Documents.Wire.Path;
using ProjectService = EtAlii.Adp.Projects.Wire.ProjectService;

namespace EtAlii.Adp.Backend.Tests;

/// <summary>
/// The shipped showcase, opened over the real host exactly as the client opens it.
/// </summary>
/// <remarks>
/// <b>Written because the module shipped without it and a user found the gap.</b> Every
/// comparable module has one of these - ansible, azure-pipeline and c4 all do - and this one
/// did not, so nothing in the suite ever opened this type end to end. The unit tests proved the
/// readers, the derivation and the canvas separately, and all of them were right; what none of
/// them touched was the whole path from an .adp on disk to elements on the wire.
/// <para>
/// <b>The assertion is the count and the type strings, because those are the agreement that can
/// silently disagree.</b> The backend's element types and the client's ELEMENT_TYPES set are two
/// copies of one contract, and a module whose halves are each tested against their own idea of
/// it passes twice while the diagram draws nothing.
/// </para>
/// </remarks>
public class DotNetDependencyGraphFlowTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private const string DeveloperUsername = "admin";
    private const string DeveloperCredential = "changeme";
    private const string SessionTokenHeader = "session-token";

    private readonly WebApplicationFactory<Program> _factory;
    private readonly string _appDataRoot;
    private readonly string _projectFolder;

    public DotNetDependencyGraphFlowTests(WebApplicationFactory<Program> baseFactory)
    {
        _appDataRoot = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.IntegrationTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_appDataRoot);
        _projectFolder = ShowcaseFolder();

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
                    provider.GetRequiredService<EtAlii.Adp.Diagram.DiagramValidators>()));
            });
        });
    }

    public void Dispose()
    {
        _factory.Dispose();
        TestFolder.TryDelete(_appDataRoot);
        GC.SuppressFinalize(this);
    }

    private static string ShowcaseFolder()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = IoPath.Combine(
                directory.FullName, "src", "examples", "diagrams", "dotnet-dependency-graph", "pipeline-toolkit");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new DirectoryNotFoundException("the showcase folder was not found");
    }

    /// <summary>
    /// The same showcase, opened the way a USER opens it: the whole <c>src/examples</c> tree as
    /// the project, with the diagram four folders down inside it.
    /// </summary>
    /// <remarks>
    /// <b>The fact below cannot discriminate, and this one can.</b> Adding
    /// <c>pipeline-toolkit</c> itself as the project puts the registration, the solution and
    /// every project file in or under one folder - so "resolve relative to the solution" and
    /// "resolve relative to the registration" give identical answers there, and a wrong base
    /// would pass indefinitely. This moves the project root four levels up without moving the
    /// subject, which is the arrangement the shipped showcase is actually browsed in.
    /// </remarks>
    [Fact]
    public async Task TheShowcase_OpensWithTheWholeExamplesTreeAsTheProject()
    {
        var httpClient = _factory.CreateDefaultClient();
        using var channel = GrpcChannel.ForAddress(httpClient.BaseAddress!, new GrpcChannelOptions { HttpClient = httpClient });

        var auth = new AuthenticationService.AuthenticationServiceClient(channel);
        var login = await auth.LoginAsync(
            new LoginRequest { Username = DeveloperUsername, Credential = DeveloperCredential },
            cancellationToken: TestContext.Current.CancellationToken);
        var headers = new Metadata { { SessionTokenHeader, login.Session.Value } };

        // src/examples, four levels above the diagram.
        var examplesRoot = IoPath.GetFullPath(IoPath.Combine(_projectFolder, "..", "..", ".."));
        var projects = new ProjectService.ProjectServiceClient(channel);
        var folder = new Path();
        folder.Segments.AddRange(examplesRoot.Split(IoPath.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries));
        var added = await projects.AddProjectAsync(
            new AddProjectRequest { Path = folder }, headers, cancellationToken: TestContext.Current.CancellationToken);

        var diagramPath = new Path();
        diagramPath.Segments.Add("diagrams");
        diagramPath.Segments.Add("dotnet-dependency-graph");
        diagramPath.Segments.Add("pipeline-toolkit");
        diagramPath.Segments.Add("PipelineToolkit.adp");

        var diagrams = new DiagramService.DiagramServiceClient(channel);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(30));

        var delivered = new List<EtAlii.Adp.Documents.Wire.Element>();
        using var stream = diagrams.Open(
            new OpenDiagramRequest
            {
                ProjectId = added.Added.Id,
                WatchId = ShortGuid.NewShortGuid(),
                Path = diagramPath,
            },
            headers,
            cancellationToken: cts.Token);

        while (await stream.ResponseStream.MoveNext(cts.Token))
        {
            var delta = stream.ResponseStream.Current;
            if (delta.Add is not null)
            {
                delivered.AddRange(delta.Add.Elements);
            }

            if (delivered.Count > 0)
            {
                break;
            }
        }

        Assert.Equal(4, delivered.Count(element => element.Type == "dotnet/dependency-graph+project"));
        Assert.Equal(3, delivered.Count(element => element.Type == "dotnet/dependency-graph+package"));
        Assert.Equal(9, delivered.Count(element => element.Type == "dotnet/dependency-graph+edge"));
    }

    [Fact]
    public async Task TheShippedShowcase_StreamsItsProjectsPackagesAndEdges()
    {
        var httpClient = _factory.CreateDefaultClient();
        using var channel = GrpcChannel.ForAddress(httpClient.BaseAddress!, new GrpcChannelOptions { HttpClient = httpClient });

        var auth = new AuthenticationService.AuthenticationServiceClient(channel);
        var login = await auth.LoginAsync(
            new LoginRequest { Username = DeveloperUsername, Credential = DeveloperCredential },
            cancellationToken: TestContext.Current.CancellationToken);
        var headers = new Metadata { { SessionTokenHeader, login.Session.Value } };

        var projects = new ProjectService.ProjectServiceClient(channel);
        var folder = new Path();
        folder.Segments.AddRange(_projectFolder.Split(IoPath.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries));
        var added = await projects.AddProjectAsync(
            new AddProjectRequest { Path = folder }, headers, cancellationToken: TestContext.Current.CancellationToken);

        var diagramPath = new Path();
        diagramPath.Segments.Add("PipelineToolkit.adp");

        var diagrams = new DiagramService.DiagramServiceClient(channel);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(30));

        // Act.
        var delivered = new List<EtAlii.Adp.Documents.Wire.Element>();
        using var stream = diagrams.Open(
            new OpenDiagramRequest
            {
                ProjectId = added.Added.Id,
                WatchId = ShortGuid.NewShortGuid(),
                Path = diagramPath,
            },
            headers,
            cancellationToken: cts.Token);

        while (await stream.ResponseStream.MoveNext(cts.Token))
        {
            var delta = stream.ResponseStream.Current;
            if (delta.Add is not null)
            {
                delivered.AddRange(delta.Add.Elements);
            }

            if (delivered.Count > 0)
            {
                // The baseline is the whole document, so one add delta is the answer. Reading
                // on would block until the watcher pushed something, which is not what this
                // asserts.
                break;
            }
        }

        // Assert.
        // Four projects, three packages and nine references - the shipped example's whole
        // content. A count rather than "not empty": the user's report was a diagram drawing
        // NOTHING, and "some elements arrived" would pass for a graph missing its edges too.
        Assert.Equal(4, delivered.Count(element => element.Type == "dotnet/dependency-graph+project"));
        Assert.Equal(3, delivered.Count(element => element.Type == "dotnet/dependency-graph+package"));
        Assert.Equal(9, delivered.Count(element => element.Type == "dotnet/dependency-graph+edge"));

        // The type strings themselves, spelled out rather than referenced from the mapper's
        // constants. THE POINT IS THAT THEY ARE A SECOND COPY: the client holds these same
        // three literals in its ELEMENT_TYPES set, and a test written against the constant
        // would follow a rename that the client did not, which is the one failure this pairing
        // exists to catch.
        Assert.All(delivered, element => Assert.StartsWith("dotnet/dependency-graph+", element.Type, StringComparison.Ordinal));

        // Ids the client can read both ends of an edge from - the module builds an edge id as
        // `depends:<from>-><to>` and the canvas splits it rather than carrying the ends twice.
        Assert.All(
            delivered.Where(element => element.Type == "dotnet/dependency-graph+edge"),
            element =>
            {
                ArgumentNullException.ThrowIfNull(element);

                Assert.StartsWith("depends:", element.Id.Value, StringComparison.Ordinal);
                Assert.Contains("->", element.Id.Value, StringComparison.Ordinal);
            });
    }
}
