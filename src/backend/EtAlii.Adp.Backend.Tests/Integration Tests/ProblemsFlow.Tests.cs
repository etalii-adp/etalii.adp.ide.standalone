using EtAlii.Adp.Authentication.Wire;
using EtAlii.Adp.Context.Wire;
using EtAlii.Adp.Diagram;
using EtAlii.Adp.Documents.Wire;
using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.Hierarchy.Wire;
using EtAlii.Adp.Problems;
using EtAlii.Adp.Projects;
using EtAlii.Adp.Projects.Wire;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;
using HierarchyService = EtAlii.Adp.Hierarchy.Wire.HierarchyService;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here
using Path = EtAlii.Adp.Documents.Wire.Path;
using ProjectService = EtAlii.Adp.Projects.Wire.ProjectService;

namespace EtAlii.Adp.Backend.Tests;

/// <summary>
/// The whole problems engine against the real host: the NEVER_VALIDATED baseline, Validate
/// all producing exactly the expected problems and pushing them to a second connection,
/// Validate on a folder touching only that folder, deletion dropping and rename carrying
/// entries, and the project folder byte-for-byte unchanged throughout.
/// </summary>
public class ProblemsFlowTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private const string DeveloperUsername = "admin";
    private const string DeveloperCredential = "changeme";
    private const string SessionTokenHeader = "session-token";
    private static readonly TimeSpan MessageTimeout = TimeSpan.FromSeconds(10);

    private readonly WebApplicationFactory<Program> _factory;
    private readonly string _appDataRoot;
    private readonly string _projectFolder;

    public ProblemsFlowTests(WebApplicationFactory<Program> baseFactory)
    {
        _appDataRoot = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.IntegrationTests", Guid.NewGuid().ToString("N"));
        _projectFolder = IoPath.Combine(_appDataRoot, "sample-project");
        Directory.CreateDirectory(_projectFolder);

        _factory = baseFactory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("developer");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IProjectStore>();
                services.AddSingleton<IProjectStore>(new FileProjectStore(_appDataRoot));
                // The problem cache must live and die with this test, not in the real
                // user profile the host's AddProblems registration points at.
                services.RemoveAll<IProblemStore>();
                services.AddSingleton<IProblemStore>(provider => new ProblemStore(
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

    // ---- the flows ---------------------------------------------------------------------

    [Fact]
    public async Task TheBaseline_CarriesNeverValidated()
    {
        // Arrange.
        using var session = await OpenAsync();
        using var cts = CreateMessageTimeout();

        // Act.
        var problems = await ReadProblemsAsync(session.Stream, cts.Token);

        // Assert.
        Assert.Equal(ProblemSetState.NeverValidated, problems.State);
        Assert.Empty(problems.Problems);
    }

    [Fact]
    public async Task ValidateAll_ProducesTheExpectedProblems_AndASecondConnectionReadsTheSameSet()
    {
        // Arrange.
        SeedPair("good");
        await File.WriteAllTextAsync(IoPath.Combine(_projectFolder, "strange.adp"), "vendor/unheard-of\n", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(IoPath.Combine(_projectFolder, "empty.adp"), "", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(IoPath.Combine(_projectFolder, "notes.txt"), "just notes", TestContext.Current.CancellationToken);
        var before = Snapshot();

        // Arrange, continued.
        using var session = await OpenAsync();
        using var second = await OpenAsync(session);
        using (var baselines = CreateMessageTimeout())
        {
            await ReadProblemsAsync(session.Stream, baselines.Token);
            await ReadProblemsAsync(second.Stream, baselines.Token);
        }

        // Arrange, continued.
        await ExecuteAsync(session, ValidateAllContextActionProvider.ValidateAllActionId, ProblemsSource());

        // Arrange, continued.
        using var cts = CreateMessageTimeout();
        var mine = await ReadValidatedProblemsAsync(session.Stream, cts.Token);
        var theirs = await ReadValidatedProblemsAsync(second.Stream, cts.Token);

        // Act.
        foreach (var set in new[] { mine, theirs })
        {
            Assert.Equal(2, set.Problems.Count); // the unknown type and the unreadable one - never the .txt or the healthy pair
            Assert.Equal(2u, set.ErrorCount);
            Assert.Equal(0u, set.WarningCount);
            Assert.Contains(set.Problems, problem => problem.RuleId == "core.unknown-type" && problem.Path.Segments.Single() == "strange.adp");
            Assert.Contains(set.Problems, problem => problem.RuleId == "core.unreadable" && problem.Path.Segments.Single() == "empty.adp");
        }

        // Assert.
        Assert.Equal(before, Snapshot()); // Requirement 6.7: validation never writes a project file.
    }

    [Fact]
    public async Task Validate_OnAFolder_TouchesOnlyThatFolder()
    {
        // Arrange.
        Directory.CreateDirectory(IoPath.Combine(_projectFolder, "inside"));
        await File.WriteAllTextAsync(IoPath.Combine(_projectFolder, "inside", "bad.adp"), "vendor/unheard-of\n", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(IoPath.Combine(_projectFolder, "also-bad.adp"), "vendor/unheard-of\n", TestContext.Current.CancellationToken);

        using var session = await OpenAsync();
        using (var baseline = CreateMessageTimeout())
        {
            await ReadProblemsAsync(session.Stream, baseline.Token);
        }
        var folderId = await EntryIdAsync(session, "inside");

        await ExecuteAsync(session, ValidateContextActionProvider.ValidateActionId, new ContextSource { EntryId = folderId });

        // Act and assert, step by step.
        using var cts = CreateMessageTimeout();
        var set = await ReadValidatedProblemsAsync(session.Stream, cts.Token);
        var problem = Assert.Single(set.Problems);
        Assert.Equal(["inside", "bad.adp"], problem.Path.Segments); // also-bad.adp was outside the scope and stays unjudged
    }

    [Fact]
    public async Task DeletingADiagram_DropsItsProblems()
    {
        // Arrange.
        await File.WriteAllTextAsync(IoPath.Combine(_projectFolder, "doomed.adp"), "vendor/unheard-of\n", TestContext.Current.CancellationToken);

        // Act and assert, step by step.
        using var session = await OpenAsync();
        using (var baseline = CreateMessageTimeout())
        {
            await ReadProblemsAsync(session.Stream, baseline.Token);
        }
        await ExecuteAsync(session, ValidateAllContextActionProvider.ValidateAllActionId, ProblemsSource());
        using (var validated = CreateMessageTimeout())
        {
            var set = await ReadValidatedProblemsAsync(session.Stream, validated.Token);
            Assert.Single(set.Problems);
        }

        File.Delete(IoPath.Combine(_projectFolder, "doomed.adp"));

        using var cts = CreateMessageTimeout();
        var after = await ReadUntilAsync(session.Stream, cts.Token, problems => problems.Problems.Count == 0);
        Assert.Equal(0u, after.ErrorCount);
    }

    [Fact]
    public async Task RenamingADiagram_CarriesItsProblems()
    {
        // Arrange.
        await File.WriteAllTextAsync(IoPath.Combine(_projectFolder, "old.adp"), "vendor/unheard-of\n", TestContext.Current.CancellationToken);

        using var session = await OpenAsync();
        using (var baseline = CreateMessageTimeout())
        {
            await ReadProblemsAsync(session.Stream, baseline.Token);
        }
        await ExecuteAsync(session, ValidateAllContextActionProvider.ValidateAllActionId, ProblemsSource());
        using (var validated = CreateMessageTimeout())
        {
            await ReadValidatedProblemsAsync(session.Stream, validated.Token);
        }

        File.Move(IoPath.Combine(_projectFolder, "old.adp"), IoPath.Combine(_projectFolder, "new.adp"));

        // Act and assert, step by step.
        using var cts = CreateMessageTimeout();
        var after = await ReadUntilAsync(session.Stream, cts.Token,
            problems => problems.Problems.Count == 1 && problems.Problems[0].Path.Segments.Single() == "new.adp");
        // Carried, not re-reported as unchecked: still the same verdict, at the new path.
        Assert.Equal("core.unknown-type", after.Problems[0].RuleId);
    }

    // ---- plumbing ----------------------------------------------------------------------

    private async Task<ProblemsFlowSession> OpenAsync(ProblemsFlowSession? existing = null)
    {
        var channel = CreateChannel();
        var headers = await LoginAsync(channel);
        var projectId = existing?.ProjectId ?? await AddProjectAsync(channel, headers);
        var watchId = ShortGuid.NewShortGuid();
        var contextClient = new ContextService.ContextServiceClient(channel);
        var call = contextClient.Watch(new WatchContextRequest { ProjectId = projectId, WatchId = watchId }, headers, cancellationToken: TestContext.Current.CancellationToken);
        return new ProblemsFlowSession { Channel = channel, Headers = headers, ProjectId = projectId, WatchId = watchId, Call = call };
    }

    private static async Task ExecuteAsync(ProblemsFlowSession session, string actionId, ContextSource source)
    {
        var contextClient = new ContextService.ContextServiceClient(session.Channel);
        var response = await contextClient.ExecuteActionAsync(
            new ExecuteActionRequest
            {
                ProjectId = session.ProjectId,
                WatchId = session.WatchId,
                Source = source,
                InteractionId = ShortGuid.NewShortGuid(),
                ActionId = actionId,
            },
            session.Headers,
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(response.Accepted, $"Executing {actionId} was rejected: {response.Error}");
    }

    private async Task<ShortGuid> EntryIdAsync(ProblemsFlowSession session, string name)
    {
        var hierarchyClient = new HierarchyService.HierarchyServiceClient(session.Channel);
        var entries = await hierarchyClient.ListEntriesAsync(
            new ListEntriesRequest { ProjectId = session.ProjectId, WatchId = session.WatchId },
            session.Headers,
            cancellationToken: TestContext.Current.CancellationToken);
        return entries.Entries.Entries_.Single(entry => entry.Name == name).Id;
    }

    private static ContextSource ProblemsSource() => new() { Problems = new Google.Protobuf.WellKnownTypes.Empty() };

    private static async Task<ProjectProblems> ReadProblemsAsync(IAsyncStreamReader<ContextMessage> stream, CancellationToken cancellationToken)
    {
        while (await stream.MoveNext(cancellationToken))
        {
            if (stream.Current.MessageCase == ContextMessage.MessageOneofCase.Problems)
            {
                return stream.Current.Problems;
            }
        }

        throw new InvalidOperationException("The stream ended before a problems message arrived.");
    }

    /// <summary>Reads past the VALIDATING push to the settled VALIDATED answer.</summary>
    private static Task<ProjectProblems> ReadValidatedProblemsAsync(IAsyncStreamReader<ContextMessage> stream, CancellationToken cancellationToken) =>
        ReadUntilAsync(stream, cancellationToken, problems => problems.State == ProblemSetState.Validated);

    private static async Task<ProjectProblems> ReadUntilAsync(
        IAsyncStreamReader<ContextMessage> stream, CancellationToken cancellationToken, Func<ProjectProblems, bool> accept)
    {
        while (true)
        {
            var problems = await ReadProblemsAsync(stream, cancellationToken);
            if (accept(problems))
            {
                return problems;
            }
        }
    }

    private void SeedPair(string baseName)
    {
        File.WriteAllText(IoPath.Combine(_projectFolder, baseName + ".adp"), "freeplane/mindmap\n");
        File.WriteAllText(IoPath.Combine(_projectFolder, baseName + ".mm"), "<map version=\"freeplane 1.11.5\"><node TEXT=\"root\"/></map>");
    }

    private Dictionary<string, byte[]> Snapshot() =>
        Directory.GetFiles(_projectFolder, "*", SearchOption.AllDirectories)
            .ToDictionary(path => path, File.ReadAllBytes);

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
