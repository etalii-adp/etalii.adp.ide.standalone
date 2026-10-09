using EtAlii.Adp.History;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EtAlii.Adp.Diagram.AgentActivityDiagram.Tests;

/// <summary>
/// A file an agent wrote that breaks a rule (agent-activity-diagram Requirements 2.8, 2.9, 3.8, 5.6,
/// 8.4, 8.5, 8.7, 8.8 and 10.6): one fixture per finding, each of which still opens, draws what
/// it can, says what is wrong where, and is written back as it was.
/// </summary>
public sealed class AadFindingsTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("aad-findings-").FullName;
    private readonly ServiceProvider _services = Services();

    private string Body => Path.Combine(_root, "work.aad");

    private static ServiceProvider Services()
    {
        var services = new ServiceCollection().AddCommands();
        services.AddAgentActivityDiagram();
        return services.BuildServiceProvider();
    }

    public void Dispose()
    {
        _services.Dispose();
        Directory.Delete(_root, recursive: true);
    }

    private static string Fixture(string name) => AadDocumentTests.Fixture(Path.Combine("findings", name + ".aad"));

    [Theory]
    [InlineData("dangling-reference", "fbl.dangling-reference", DiagramProblemSeverity.Error, 6)]
    [InlineData("duplicate-id", "std.duplicateId", DiagramProblemSeverity.Error, 6)]
    [InlineData("link-not-openable", "aad.link-not-openable", DiagramProblemSeverity.Warning, 4)]
    [InlineData("unknown-key", "aad.unknown-key", DiagramProblemSeverity.Info, 4)]
    [InlineData("missing-id", "std.missingId", DiagramProblemSeverity.Info, 4)]
    [InlineData("newer-version", "fbl.header-mismatch", DiagramProblemSeverity.Error, 1)]
    public void ABreach_IsSaidWhereItIs_AtItsWeight_AndTheFileStillOpensAndIsKept(string fixture, string rule, DiagramProblemSeverity severity, int line)
    {
        // Arrange.
        var text = Fixture(fixture);

        // Act.
        var entry = AadDocumentEntry.Read(text);
        var problems = AadValidator.Validate(entry.Document);

        // Assert: said once, at its weight, on its line; everything readable is there; nothing is rewritten.
        var found = Assert.Single(problems, problem => problem.RuleId == rule);
        Assert.Equal(severity, found.Severity);
        Assert.Equal(new DiagramProblemLineLocation((uint)line), found.Location);
        Assert.True(entry.IsUsable, entry.Unreadable);
        Assert.NotEmpty(entry.Model.Elements);
        Assert.Equal(text, entry.Document.Text);
    }

    [Fact]
    public async Task AFileOfANewerVersion_IsShownAndNeverChanged()
    {
        // Arrange.
        var text = Fixture("newer-version");
        await File.WriteAllTextAsync(Body, text, TestContext.Current.CancellationToken);

        // Act.
        var result = await _services.GetRequiredService<IHistoryStackStore>().Get(_root).ExecuteAsync(new SetAadAttributeCommand(Body, "p", "name", "Renamed"), TestContext.Current.CancellationToken);

        // Assert.
        Assert.False(result.IsSuccess);
        Assert.Contains("version 2", result.Error, StringComparison.Ordinal);
        Assert.Equal(text, await File.ReadAllTextAsync(Body, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task WhatWasSetForAnElementAnAgentRemoved_IsNotReported_AndGoesWithTheNextWrite()
    {
        // Arrange.
        var text = Fixture("orphaned-lock");
        await File.WriteAllTextAsync(Body, text, TestContext.Current.CancellationToken);

        // Assert: nothing is said about the lock and the group state of the element that is gone.
        Assert.DoesNotContain(AadValidator.Validate(AadBody.Parse(text)), problem => problem.RuleId == "fbl.dangling-reference");

        // Act: any edit made in ADP.
        var result = await _services.GetRequiredService<IHistoryStackStore>().Get(_root).ExecuteAsync(new SetAadAttributeCommand(Body, "p", "name", "Renamed"), TestContext.Current.CancellationToken);

        // Assert: the edit, and the two entries about nothing gone with it; the lock that names an element stays.
        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(
            "agent-activity-diagram: 1\r\n# A lock and a group state left behind by an element an agent removed.\r\nprojects:\r\n  - id: p\r\n    name: Renamed\r\nview:\r\n  placements:\r\n    - element: p\r\n      x: 10\r\n      y: 20\r\n",
            await File.ReadAllTextAsync(Body, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AFileThatCanNoLongerBeRead_KeepsShowingWhatWasLastRead_AndIsNotWritten()
    {
        // Arrange: a session with a readable file open.
        var token = TestContext.Current.CancellationToken;
        await File.WriteAllTextAsync(Body, AadDocumentTests.Fixture("one-day.aad"), token);
        var documents = _services.GetRequiredService<IAadDocumentStore>();
        await using var session = new AadSession(Body, documents, new AadElementMapper());
        var before = session.Baseline().OfType<DiagramAddDelta>().Sum(delta => delta.Elements.Count);

        // Act: an agent leaves the file half written.
        const string broken = "agent-activity-diagram: 1\r\nprojects: [\r\n";
        await File.WriteAllTextAsync(Body, broken, token);
        documents.Reload(Body);
        var still = session.UpdateView(DiagramViewport.Unbounded);
        var shown = session.Baseline().OfType<DiagramAddDelta>().Sum(delta => delta.Elements.Count);
        var edit = await _services.GetRequiredService<IHistoryStackStore>().Get(_root).ExecuteAsync(new SetAadAttributeCommand(Body, "p-adp", "name", "Renamed"), token);

        // Assert: the open diagram is told to remove nothing, the file says why it cannot be read, and nothing writes it.
        Assert.True(before > 5);
        Assert.DoesNotContain(still, delta => delta is DiagramRemoveDelta);
        Assert.Equal(before, shown);
        Assert.Contains(AadValidator.Validate(AadBody.Parse(broken)), problem => problem.Severity == DiagramProblemSeverity.Error && problem.Message.Contains("YAML", StringComparison.Ordinal));
        Assert.False(edit.IsSuccess);
        Assert.Equal(broken, await File.ReadAllTextAsync(Body, token));
    }
}
