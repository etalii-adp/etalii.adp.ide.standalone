using EtAlii.Adp.Context.Wire;
using EtAlii.Adp.Documents.Wire;
using EtAlii.Adp.Hierarchy.Wire;
using EtAlii.Adp.Problems;
using EtAlii.Adp.TestSupport.Host;
using Grpc.Core;
using Xunit;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Backend.Tests;

/// <summary>
/// The whole problems engine against the real host: the NEVER_VALIDATED baseline, Validate
/// all producing exactly the expected problems and pushing them to a second connection,
/// Validate on a folder touching only that folder, deletion dropping and rename carrying
/// entries, and the project folder byte-for-byte unchanged throughout.
/// </summary>
public class ProblemsFlowTests(HostFixture host) : IClassFixture<HostFixture>
{
    private readonly string _projectFolder = host.NewProjectFolder();

    // ---- the flows ---------------------------------------------------------------------

    [Fact]
    public async Task TheBaseline_CarriesNeverValidated()
    {
        // Arrange.
        using var session = await OpenAsync();
        using var cts = FlowSession.CreateMessageTimeout(TestContext.Current.CancellationToken);

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
        using (var baselines = FlowSession.CreateMessageTimeout(TestContext.Current.CancellationToken))
        {
            await ReadProblemsAsync(session.Stream, baselines.Token);
            await ReadProblemsAsync(second.Stream, baselines.Token);
        }

        // Arrange, continued.
        await ExecuteAsync(session, ValidateAllContextActionProvider.ValidateAllActionId, ProblemsSource());

        // Arrange, continued.
        using var cts = FlowSession.CreateMessageTimeout(TestContext.Current.CancellationToken);
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
        using (var baseline = FlowSession.CreateMessageTimeout(TestContext.Current.CancellationToken))
        {
            await ReadProblemsAsync(session.Stream, baseline.Token);
        }
        var folderId = await EntryIdAsync(session, "inside");

        await ExecuteAsync(session, ValidateContextActionProvider.ValidateActionId, new ContextSource { EntryId = folderId });

        // Act and assert, step by step.
        using var cts = FlowSession.CreateMessageTimeout(TestContext.Current.CancellationToken);
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
        using (var baseline = FlowSession.CreateMessageTimeout(TestContext.Current.CancellationToken))
        {
            await ReadProblemsAsync(session.Stream, baseline.Token);
        }
        await ExecuteAsync(session, ValidateAllContextActionProvider.ValidateAllActionId, ProblemsSource());
        using (var validated = FlowSession.CreateMessageTimeout(TestContext.Current.CancellationToken))
        {
            var set = await ReadValidatedProblemsAsync(session.Stream, validated.Token);
            Assert.Single(set.Problems);
        }

        File.Delete(IoPath.Combine(_projectFolder, "doomed.adp"));

        using var cts = FlowSession.CreateMessageTimeout(TestContext.Current.CancellationToken);
        var after = await ReadUntilAsync(session.Stream, cts.Token, problems => problems.Problems.Count == 0);
        Assert.Equal(0u, after.ErrorCount);
    }

    [Fact]
    public async Task RenamingADiagram_CarriesItsProblems()
    {
        // Arrange.
        await File.WriteAllTextAsync(IoPath.Combine(_projectFolder, "old.adp"), "vendor/unheard-of\n", TestContext.Current.CancellationToken);

        using var session = await OpenAsync();
        using (var baseline = FlowSession.CreateMessageTimeout(TestContext.Current.CancellationToken))
        {
            await ReadProblemsAsync(session.Stream, baseline.Token);
        }
        await ExecuteAsync(session, ValidateAllContextActionProvider.ValidateAllActionId, ProblemsSource());
        using (var validated = FlowSession.CreateMessageTimeout(TestContext.Current.CancellationToken))
        {
            await ReadValidatedProblemsAsync(session.Stream, validated.Token);
        }

        File.Move(IoPath.Combine(_projectFolder, "old.adp"), IoPath.Combine(_projectFolder, "new.adp"));

        // Act and assert, step by step.
        using var cts = FlowSession.CreateMessageTimeout(TestContext.Current.CancellationToken);
        var after = await ReadUntilAsync(session.Stream, cts.Token,
            problems => problems.Problems.Count == 1 && problems.Problems[0].Path.Segments.Single() == "new.adp");
        // Carried, not re-reported as unchecked: still the same verdict, at the new path.
        Assert.Equal("core.unknown-type", after.Problems[0].RuleId);
    }

    // ---- plumbing ----------------------------------------------------------------------

    private async Task<ProblemsFlowSession> OpenAsync(ProblemsFlowSession? existing = null)
    {
        var flow = existing is null
            ? await FlowSession.OpenAsync(host, _projectFolder, TestContext.Current.CancellationToken)
            : await FlowSession.JoinAsync(host, existing.Flow, TestContext.Current.CancellationToken);
        var call = flow.Context.Watch(new WatchContextRequest { ProjectId = flow.ProjectId, WatchId = flow.WatchId }, flow.Headers, cancellationToken: TestContext.Current.CancellationToken);
        return new ProblemsFlowSession(flow, call);
    }

    private static async Task ExecuteAsync(ProblemsFlowSession session, string actionId, ContextSource source)
    {
        var flow = session.Flow;
        var response = await flow.Context.ExecuteActionAsync(
            new ExecuteActionRequest
            {
                ProjectId = flow.ProjectId,
                WatchId = flow.WatchId,
                Source = source,
                InteractionId = ShortGuid.NewShortGuid(),
                ActionId = actionId,
            },
            flow.Headers,
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(response.Accepted, $"Executing {actionId} was rejected: {response.Error}");
    }

    private async Task<ShortGuid> EntryIdAsync(ProblemsFlowSession session, string name)
    {
        var flow = session.Flow;
        var entries = await flow.Hierarchy.ListEntriesAsync(
            new ListEntriesRequest { ProjectId = flow.ProjectId, WatchId = flow.WatchId },
            flow.Headers,
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
}
