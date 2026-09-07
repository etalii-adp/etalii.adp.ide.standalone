using System.Text;
using EtAlii.Adp.Common;
using EtAlii.Adp.Common.Wire;
using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.History;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.Rdf.Tests;

/// <summary>
/// The action surface: discovery per selection kind, prefix-validated dialogs, every real edit
/// one undo away through the real command pipeline, and every edit withheld under truncation
/// with the stated sentence (rdf-diagram Requirements 5.7, 6 and 8.4).
/// </summary>
public class RdfContextActionProviderTests : IDisposable
{
    private const string Corpus =
        "@prefix ex: <http://example.org/> .\r\n"
        + "\r\n"
        + "ex:alice ex:knows ex:bob ;\r\n"
        + "    ex:name \"Alice\" .\r\n"
        + "\r\n"
        + "ex:bob ex:name \"Bob\" .\r\n";

    private readonly string _root;
    private readonly ServiceProvider _provider;
    private readonly RdfContextActionProvider _actions;
    private readonly IHistoryStack _history;

    public RdfContextActionProviderTests()
    {
        _root = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _provider = new ServiceCollection()
            .AddSingleton<IReadOnlyList<DiagramDefinition>>(Diagram.Definitions)
            .AddCommands().AddHierarchyCommandHandlers()
            .AddRdf()
            .BuildServiceProvider();
        _actions = new RdfContextActionProvider(
            _provider.GetRequiredService<IHistoryStackStore>(),
            _provider.GetRequiredService<IRdfDocumentStore>());
        _history = _provider.GetRequiredService<IHistoryStackStore>().Get(_root);
    }

    public void Dispose()
    {
        _provider.Dispose();
        TestFolder.TryDelete(_root);
    }

    private string WriteBody(string content, string name = "graph.ttl")
    {
        var path = IoPath.Combine(_root, name);
        File.WriteAllText(path, content);
        return path;
    }

    private ContextTarget Target(string bodyPath, string elementId) => new(
        ContextScope.DiagramElement, bodyPath, IsContainer: false, SourceId: default, _root, default, elementId);

    private async Task<IReadOnlyList<string>> ActionIds(string bodyPath, string elementId)
    {
        var groups = await _actions.DiscoverAsync(Target(bodyPath, elementId), TestContext.Current.CancellationToken);
        return groups.SelectMany(group => group.Actions).Select(action => action.Id).ToList();
    }

    [Fact]
    public async Task DiscoveryPerKind_MatchesTheSelection()
    {
        // Arrange.
        var body = WriteBody(Corpus);

        // Act & assert.
        var resource = await ActionIds(body, "res:http://example.org/alice");
        Assert.Contains(RdfContextActionProvider.RenameResourceActionId, resource);
        Assert.Contains(RdfContextActionProvider.RemoveResourceActionId, resource);

        Assert.Contains(
            RdfContextActionProvider.RemoveEdgeActionId,
            await ActionIds(body, "edge:res:http://example.org/alice|http://example.org/knows|res:http://example.org/bob"));

        Assert.Contains(RdfContextActionProvider.AddResourceActionId, await ActionIds(body, "new:100,200"));
        Assert.Contains(RdfContextActionProvider.ConnectActionId, await ActionIds(body, "rel:res:http://example.org/alice->res:http://example.org/bob"));

        // A vanished element and a blank node both discover nothing to do.
        Assert.Empty(await ActionIds(body, "res:http://example.org/nobody"));
    }

    [Fact]
    public async Task Rename_AsksValidatesAndCommits_OneUndoAway()
    {
        // Arrange.
        var body = WriteBody(Corpus);
        var target = Target(body, "res:http://example.org/bob");

        // Act: the dialog opens prefilled with the compressed name.
        var execution = await _actions.ExecuteAsync(target, RdfContextActionProvider.RenameResourceActionId, TestContext.Current.CancellationToken);
        var input = Assert.IsType<ContextExecutionRequiresInput>(execution);
        Assert.Equal("ex:bob", input.Request.InitialValue);

        // An undeclared prefix is refused by name before any splice (Requirement 5.7).
        var unknown = await _actions.ValidateAsync(target, RdfContextActionProvider.RenameResourceActionId, "nope:bob", TestContext.Current.CancellationToken);
        Assert.False(unknown.Valid);
        Assert.Contains("nope", unknown.Reason);

        // Renaming onto an existing term would silently merge two resources.
        var collision = await _actions.ValidateAsync(target, RdfContextActionProvider.RenameResourceActionId, "ex:alice", TestContext.Current.CancellationToken);
        Assert.False(collision.Valid);

        // Act: a declared-prefix name commits.
        var commit = await _actions.CommitAsync(target, RdfContextActionProvider.RenameResourceActionId, "ex:robert", "", TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(commit.Completed);
        var text = await File.ReadAllTextAsync(body, TestContext.Current.CancellationToken);
        Assert.Contains("ex:robert ex:name", text);
        Assert.DoesNotContain("ex:bob", text);

        // One undo restores the bytes.
        await _history.UndoAsync(TestContext.Current.CancellationToken);
        Assert.Equal(Corpus, await File.ReadAllTextAsync(body, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RemoveResource_SaysHowManyStatementsGoWithIt_ThenCommits()
    {
        // Arrange: alice appears in two statements.
        var body = WriteBody(Corpus);
        var target = Target(body, "res:http://example.org/alice");

        // Act.
        var execution = await _actions.ExecuteAsync(target, RdfContextActionProvider.RemoveResourceActionId, TestContext.Current.CancellationToken);

        // Assert: the confirmation names the count before anything runs.
        var confirmation = Assert.IsType<ContextExecutionRequiresConfirmation>(execution);
        Assert.Contains("2 statements", confirmation.Request.Message);

        // Act: confirming commits, as one undo.
        var commit = await _actions.CommitAsync(target, RdfContextActionProvider.RemoveResourceActionId, "", "", TestContext.Current.CancellationToken);
        Assert.True(commit.Completed);
        Assert.DoesNotContain("alice", await File.ReadAllTextAsync(body, TestContext.Current.CancellationToken), StringComparison.Ordinal);

        await _history.UndoAsync(TestContext.Current.CancellationToken);
        Assert.Equal(Corpus, await File.ReadAllTextAsync(body, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task TheRelationGesture_AsksForThePredicate_AndStatesTheTriple()
    {
        // Arrange.
        var body = WriteBody(Corpus);
        var target = Target(body, "rel:res:http://example.org/bob->res:http://example.org/alice");

        // Act.
        var execution = await _actions.ExecuteAsync(target, RdfContextActionProvider.ConnectActionId, TestContext.Current.CancellationToken);
        Assert.IsType<ContextExecutionRequiresInput>(execution);
        var commit = await _actions.CommitAsync(target, RdfContextActionProvider.ConnectActionId, "ex:knows", "", TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(commit.Completed);
        Assert.Contains("ex:knows ex:alice", await File.ReadAllTextAsync(body, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AddResourceAtAPlacement_StatesTheTypedResource()
    {
        // Arrange.
        var body = WriteBody(Corpus);
        var target = Target(body, "new:10,20");

        // Act.
        var commit = await _actions.CommitAsync(target, RdfContextActionProvider.AddResourceActionId, "ex:carol", "", TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(commit.Completed);
        // The rdfs namespace is undeclared, so the type is written in full - never invented.
        Assert.Contains("ex:carol a <http://www.w3.org/2000/01/rdf-schema#Resource> .", await File.ReadAllTextAsync(body, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AddPrefix_ParsesTheOneLineDeclaration()
    {
        // Arrange.
        var body = WriteBody(Corpus);
        var target = Target(body, "new:0,0");

        // Act & assert: a shapeless value is refused in validation.
        var invalid = await _actions.ValidateAsync(target, RdfContextActionProvider.AddPrefixActionId, "not a declaration", TestContext.Current.CancellationToken);
        Assert.False(invalid.Valid);

        var commit = await _actions.CommitAsync(target, RdfContextActionProvider.AddPrefixActionId, "foaf: <http://xmlns.com/foaf/0.1/>", "", TestContext.Current.CancellationToken);
        Assert.True(commit.Completed);
        Assert.Contains("@prefix foaf: <http://xmlns.com/foaf/0.1/> .", await File.ReadAllTextAsync(body, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task UnderTruncation_EveryEditIsWithheld_WithTheStatedSentence()
    {
        // Arrange: more resources than the budget draws.
        var builder = new StringBuilder("@prefix ex: <http://example.org/> .\r\n");
        for (var i = 0; i <= RdfProjection.DefaultBudget; i++)
        {
            builder.Append($"ex:s{i} ex:name \"n{i}\" .\r\n");
        }

        var body = WriteBody(builder.ToString(), "big.ttl");
        var target = Target(body, "res:http://example.org/s0");

        // Act & assert: nothing discovers, and executing anyway answers with the sentence.
        Assert.Empty(await ActionIds(body, "res:http://example.org/s0"));
        var execution = await _actions.ExecuteAsync(target, RdfContextActionProvider.RemoveResourceActionId, TestContext.Current.CancellationToken);
        var failed = Assert.IsType<ContextExecutionFailed>(execution);
        Assert.Contains("withheld", failed.Message);
    }
}
