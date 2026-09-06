using System.Text;
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

    [Fact]
    public async Task ADslOpenAsDiagramAndAsTextAtOnce_BothStayTrueToTheFile()
    {
        // Arrange: one connection, two views of one file (Requirements 5.3, 5.5). No
        // synchronisation code exists between them anywhere - each session independently
        // reads and writes the same path, so the file on disk is the tie-breaker by
        // construction, and this test is the proof that construction suffices.
        var originalText = "workspace \"W\" {\n  model {\n    p = person \"Postman\"\n  }\n  views {\n    systemLandscape \"sl\" {\n      include *\n    }\n  }\n}\n";
        await File.WriteAllTextAsync(IoPath.Combine(_projectFolder, "both.dsl"), originalText, TestContext.Current.CancellationToken);

        using var channel = CreateChannel();
        var headers = await LoginAsync(channel);
        var projectId = await AddProjectAsync(channel, headers);
        var diagramClient = new DiagramService.DiagramServiceClient(channel);
        var watchId = ShortGuid.NewShortGuid();
        var path = new Path();
        path.Segments.Add("both.dsl");

        // The diagram view: the router claims the path, exactly as it always has (R5.1).
        using var diagramCall = diagramClient.Open(new OpenDiagramRequest { ProjectId = projectId, WatchId = watchId, Path = path }, headers, deadline: DateTime.UtcNow.AddSeconds(60), cancellationToken: TestContext.Current.CancellationToken);
        var diagramBaseline = await NextAddAsync(diagramCall.ResponseStream, add => add.Elements.Count > 0);
        Assert.DoesNotContain(diagramBaseline.Elements, element => element.Id?.Value == "content");

        // The text view of the same path, on the same connection: editor_id forces the editor
        // family - the "Open as text" tab's stream (R5.2) - and both stay open at once.
        using var textCall = diagramClient.Open(new OpenDiagramRequest { ProjectId = projectId, WatchId = watchId, Path = path, EditorId = "*" }, headers, deadline: DateTime.UtcNow.AddSeconds(60), cancellationToken: TestContext.Current.CancellationToken);
        var textBaseline = await NextAddAsync(textCall.ResponseStream, add => add.Elements.Any(element => element.Id?.Value == "content"));
        Assert.Equal(originalText, ContentOf(textBaseline));

        // Act 1: save through the text wire, renaming the person.
        var editedText = originalText.Replace("Postman", "Quartermaster");
        var saved = await diagramClient.SaveTextAsync(
            new SaveTextRequest { ProjectId = projectId, WatchId = watchId, Path = path, Content = editedText },
            headers,
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("", saved.Error);

        // Assert 1a: the text session hears its own file change from disk - a Remove+Add
        // replacement, not a stale private copy.
        var textAfterSave = await NextAddAsync(textCall.ResponseStream, add => ContentOf(add) == editedText);
        Assert.Equal(editedText, ContentOf(textAfterSave));

        // Assert 1b: R5.3's other half - the diagram hears the same save as pushed deltas.
        // The DiagramDocumentReloadBridge sees the write land, the C4 store re-reads the
        // body, and the open diagram stream re-delivers its view with the renamed person in
        // it. (Task 6.2 recorded a design gap here - every store exposed Reload but nothing
        // in the codebase invoked it, so an external write never reached an open diagram -
        // and the bridge is what closed it.)
        var diagramAfterSave = await NextAddAsync(
            diagramCall.ResponseStream,
            add => add.Elements.Any(element => PayloadTextOf(element).Contains("Quartermaster", StringComparison.Ordinal)));
        Assert.DoesNotContain(diagramAfterSave.Elements, element => element.Id?.Value == "content");

        // Act 2 and assert 2: the reverse direction. A diagram-side save lands on the same
        // disk path through its own store; the write below takes that identical
        // watcher-observed route, and the text session reconciles rather than clinging to
        // what it last streamed (R5.5: the disk is the tie-breaker, always).
        var diagramSideText = editedText.Replace("Quartermaster", "Quartermistress");
        await File.WriteAllTextAsync(
            IoPath.Combine(_projectFolder, "both.dsl"), diagramSideText, TestContext.Current.CancellationToken);
        await NextAddAsync(textCall.ResponseStream, add => ContentOf(add) == diagramSideText);
    }

    /// <summary>The "content" element's text, or empty for an Add that carries none.</summary>
    private static string ContentOf(Add add)
    {
        var element = add.Elements.FirstOrDefault(candidate => candidate.Id?.Value == "content");
        return element?.Payload is { } payload ? Encoding.UTF8.GetString(payload.Value.Span) : "";
    }

    /// <summary>
    /// An element's payload bytes as text - enough to find a name inside a serialized proto
    /// payload, which carries its strings as UTF-8 verbatim.
    /// </summary>
    private static string PayloadTextOf(Element element) =>
        element.Payload is { } payload ? Encoding.UTF8.GetString(payload.Value.Span) : "";

    /// <summary>Reads the stream until an Add matches; the call's own deadline is the timeout.</summary>
    private static async Task<Add> NextAddAsync(IAsyncStreamReader<Delta> stream, Func<Add, bool> matches)
    {
        while (await stream.MoveNext(TestContext.Current.CancellationToken))
        {
            if (stream.Current.ActionCase == Delta.ActionOneofCase.Add && matches(stream.Current.Add))
            {
                return stream.Current.Add;
            }
        }

        throw new InvalidOperationException("The stream ended before the expected Add delta arrived.");
    }

    /// <summary>Opens the stream and returns the first Add delta the baseline produces.</summary>
    private static async Task<Add> FirstAddDeltaAsync(
        DiagramService.DiagramServiceClient diagramClient,
        Metadata headers,
        Common.Wire.ShortGuid projectId,
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

    private async Task<Common.Wire.ShortGuid> AddProjectAsync(GrpcChannel channel, Metadata headers)
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
