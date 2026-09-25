using EtAlii.Adp.Authentication.Wire;
using EtAlii.Adp.Context.Wire;
using EtAlii.Adp.Documents.Wire;
using EtAlii.Adp.Projects;
using EtAlii.Adp.Projects.Wire;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;
using ContextService = EtAlii.Adp.Context.Wire.ContextService;
using HierarchyService = EtAlii.Adp.Hierarchy.Wire.HierarchyService;
using IoPath = System.IO.Path;
using Path = EtAlii.Adp.Documents.Wire.Path;
using ProjectService = EtAlii.Adp.Projects.Wire.ProjectService;

namespace EtAlii.Adp.Backend.Tests;

/// <summary>
/// A .NET project node, selected through the real host, answers the property grid - whether the tab
/// was opened at the solution or at its <c>.adp</c> registration.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists.</b> <c>DotNetContextPropertyProvider</c> reads the file its target names as a
/// solution, so it is correct only if an element's <c>ContextTarget.ResolvedFullPath</c> is the
/// solution BODY. <c>DotNetContextSourceResolver</c> builds that path from the router's body, which
/// until now was established by reading the code: <see cref="DotNetDependencyGraphFlowTests"/> never
/// asks for properties, and the provider's unit guard proves its own rule, not the wiring that feeds
/// it.
/// </para>
/// <para>
/// <b>Both tabs, because only one of them can fail.</b> A tab opened at the <c>.slnx</c> hands the
/// resolver the solution as its parent, so "the parent's path" and "the routed body" are the same
/// file and a wrong base passes. A tab opened at the <c>.adp</c> hands it the registration - the case
/// centralized-selection task 26 found loading the registration as a solution and resolving nothing.
/// </para>
/// </remarks>
public class DotNetDependencyGraphPropertiesFlowTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private const string DeveloperUsername = "admin";
    private const string DeveloperCredential = "changeme";
    private const string SessionTokenHeader = "session-token";
    private const string CoreProjectId = "project:src/Pipeline.Core/Pipeline.Core.csproj";
    private static readonly TimeSpan MessageTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan StreamStartupGrace = TimeSpan.FromMilliseconds(250);

    private readonly WebApplicationFactory<Program> _factory;
    private readonly string _appDataRoot;
    private readonly string _projectFolder;

    public DotNetDependencyGraphPropertiesFlowTests(WebApplicationFactory<Program> baseFactory)
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
                    provider.GetRequiredService<Diagram.DiagramValidators>()));
            });
        });
    }

    public void Dispose()
    {
        _factory.Dispose();
        TestFolder.TryDelete(_appDataRoot);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// The shipped showcase, read only: nothing here writes, because a .NET dependency graph changes
    /// nothing in the solution it shows.
    /// </summary>
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

    [Theory]
    [InlineData("PipelineToolkit.slnx")]
    [InlineData("PipelineToolkit.adp")]
    public async Task AProjectNode_SelectedThroughTheHost_AnswersThePropertyGrid(string openedAt)
    {
        // Arrange.
        using var channel = CreateChannel();
        var headers = await LoginAsync(channel);
        var projectId = await AddProjectAsync(channel, headers);
        var watchId = ShortGuid.NewShortGuid();
        var hierarchy = new HierarchyService.HierarchyServiceClient(channel);
        var context = new ContextService.ContextServiceClient(channel);
        var entryId = await NestedEntryLookup.EntryIdOfAsync(hierarchy, projectId, watchId, headers, openedAt);

        using var cts = CreateMessageTimeout();
        using var watch = context.Watch(
            new WatchContextRequest { ProjectId = projectId, WatchId = watchId },
            headers, cancellationToken: TestContext.Current.CancellationToken);
        var pendingSelection = ReadUntilElementSelectedAsync(watch.ResponseStream, cts.Token);
        await Task.Delay(StreamStartupGrace, TestContext.Current.CancellationToken);

        // Act: the project node, selected under the file the tab was opened at.
        var selected = await context.SelectAsync(
            new SelectRequest { ProjectId = projectId, WatchId = watchId, Selection = ElementChain(entryId, CoreProjectId) },
            headers, cancellationToken: TestContext.Current.CancellationToken);

        // Assert: the selection resolved to the project...
        Assert.Equal("", selected.Error);
        var changed = await pendingSelection;
        Assert.Equal("Pipeline.Core", changed.Levels[^1].Element.Text);

        // ...and the property grid, asking the provider with the target the resolver built, reads
        // the project out of the SOLUTION - which it can only do if that target is the solution body.
        var described = await context.DescribePropertiesAsync(
            new DescribePropertiesRequest { ProjectId = projectId, WatchId = watchId },
            headers, cancellationToken: TestContext.Current.CancellationToken);

        var rows = described.Properties.ToDictionary(property => property.Id, property => property.Value);
        Assert.True(rows.Count > 0, $"Opened at {openedAt}, the project node answered no property rows.");
        Assert.Equal("Pipeline.Core", rows["name"]);
        Assert.Equal("src/Pipeline.Core/Pipeline.Core.csproj", rows["path"]);
        Assert.Equal("net10.0", rows["target-frameworks"]);
    }

    private static ContextSelection ElementChain(ShortGuid entryId, string elementId) =>
        new()
        {
            Source = ContextSelectionSource.Explorer,
            Id = new ContextSource { EntryId = entryId },
            Path = new Path(),
            Child = new ContextSelection
            {
                Source = ContextSelectionSource.DiagramCanvas,
                Id = new ContextSource { ElementId = new ElementId { Value = elementId } },
                Path = new Path(),
                None = new Empty(),
            },
        };

    /// <summary>The first pushed selection that reaches the element level - the file level alone is not the answer.</summary>
    private static async Task<ContextSelectionChanged> ReadUntilElementSelectedAsync(
        IAsyncStreamReader<ContextMessage> stream,
        CancellationToken cancellationToken)
    {
        while (await stream.MoveNext(cancellationToken))
        {
            if (stream.Current.MessageCase == ContextMessage.MessageOneofCase.Selection &&
                stream.Current.Selection.Selection is not null &&
                stream.Current.Selection.Levels.Count >= 2)
            {
                return stream.Current.Selection;
            }
        }

        throw new InvalidOperationException("The stream ended before an element selection arrived.");
    }

    private static CancellationTokenSource CreateMessageTimeout()
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(MessageTimeout);
        return cts;
    }

    private GrpcChannel CreateChannel()
    {
        var httpClient = _factory.CreateDefaultClient();
        return GrpcChannel.ForAddress(httpClient.BaseAddress!, new GrpcChannelOptions { HttpClient = httpClient });
    }

    private static async Task<Metadata> LoginAsync(GrpcChannel channel)
    {
        var auth = new AuthenticationService.AuthenticationServiceClient(channel);
        var response = await auth.LoginAsync(
            new LoginRequest { Username = DeveloperUsername, Credential = DeveloperCredential },
            cancellationToken: TestContext.Current.CancellationToken);
        return new Metadata { { SessionTokenHeader, response.Session.Value } };
    }

    private async Task<ShortGuid> AddProjectAsync(GrpcChannel channel, Metadata headers)
    {
        var projects = new ProjectService.ProjectServiceClient(channel);
        var folder = new Path();
        folder.Segments.AddRange(_projectFolder.Split(IoPath.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries));
        var response = await projects.AddProjectAsync(
            new AddProjectRequest { Path = folder }, headers, cancellationToken: TestContext.Current.CancellationToken);
        return response.Added.Id;
    }
}
