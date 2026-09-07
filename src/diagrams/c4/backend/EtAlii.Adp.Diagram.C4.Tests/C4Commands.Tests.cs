using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.History;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Diagram.C4.Tests;

/// <summary>
/// Every C4 edit is a command with an inverse, dispatched through the project's history, so a
/// change made on the canvas is one undo away like every other edit (tech.md's Commands rule,
/// c4-diagrams Requirement 13.1). And every one of them changes only the line it touches.
/// </summary>
public class C4CommandsTests : IDisposable
{
    private readonly string _root;
    private readonly string _bodyPath;
    private readonly ServiceProvider _services;
    private readonly IC4DocumentStore _documents;
    private readonly IHistoryStack _history;

    private const string Model = """
        workspace "Bank" {
            model {
                u = person "Customer" "A customer."
                s = softwareSystem "Banking" "Does banking." {
                    web = container "Web" "Serves." "React"
                }
                u -> web "Uses" "HTTPS"
            }
            views {
                container s "containers" {
                    include *
                }
            }
        }
        """;

    public C4CommandsTests()
    {
        _root = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.Diagram.C4.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _bodyPath = IoPath.Combine(_root, "model.dsl");
        File.WriteAllText(_bodyPath, Model);

        _services = new ServiceCollection().AddCommands().AddHierarchyCommandHandlers().AddC4().BuildServiceProvider();
        _documents = _services.GetRequiredService<IC4DocumentStore>();
        _history = _services.GetRequiredService<IHistoryStackStore>().Get(_root);
    }

    public void Dispose()
    {
        _services.Dispose();
        TestFolder.TryDelete(_root);
    }

    private string OnDisk() => File.ReadAllText(_bodyPath);

    private static int DifferingLines(string before, string after)
    {
        var a = before.Split('\n');
        var b = after.Split('\n');
        return a.Length != b.Length ? -1 : a.Zip(b).Count(pair => pair.First != pair.Second);
    }

    [Fact]
    public async Task Rename_ChangesTheNameAndNothingElseOnTheLine()
    {
        // Arrange.
        var before = OnDisk();

        // Act.
        var result = await _history.ExecuteAsync(new SetElementNameCommand(_bodyPath, "web", "Web Application"), TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(result.IsSuccess, result.Error);
        var after = OnDisk();
        Assert.Contains("container \"Web Application\" \"Serves.\" \"React\"", after, StringComparison.Ordinal);
        Assert.Equal(1, DifferingLines(before, after));
    }

    [Fact]
    public async Task Rename_Undoes_ToTheExactBytesItStartedFrom()
    {
        // Arrange.
        var before = OnDisk();
        await _history.ExecuteAsync(new SetElementNameCommand(_bodyPath, "web", "Renamed"), TestContext.Current.CancellationToken);

        // Act.
        await _history.UndoAsync(TestContext.Current.CancellationToken);

        // Assert.
        Assert.Equal(before, OnDisk());
    }

    [Fact]
    public async Task SetDescription_ChangesTheDescription()
    {
        // Act.
        await _history.ExecuteAsync(new SetElementDescriptionCommand(_bodyPath, "u", "A retail banking customer."), TestContext.Current.CancellationToken);

        // Assert.
        Assert.Contains("person \"Customer\" \"A retail banking customer.\"", OnDisk(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SetTechnology_OnAContainer_Works()
    {
        // Act.
        await _history.ExecuteAsync(new SetElementTechnologyCommand(_bodyPath, "web", "Java and Spring MVC"), TestContext.Current.CancellationToken);

        // Assert.
        Assert.Contains("\"Serves.\" \"Java and Spring MVC\"", OnDisk(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SetTechnology_OnAPerson_IsRefused_BecauseC4GivesThemNone()
    {
        // Arrange.
        // A person takes (name, description, tags); writing a technology into position 2 would
        // silently turn their tags into one - the same shape of bug the parser had.
        var before = OnDisk();

        // Act.
        var result = await _history.ExecuteAsync(new SetElementTechnologyCommand(_bodyPath, "u", "React"), TestContext.Current.CancellationToken);

        // Assert.
        Assert.False(result.IsSuccess);
        Assert.Contains("no technology", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(before, OnDisk());
    }

    [Fact]
    public async Task SetRelationshipDescription_RelabelsTheRelationship()
    {
        // Arrange.
        var relationshipId = _documents.WorkspaceOf(_bodyPath).Relationships.Single().Id;

        // Act.
        var result = await _history.ExecuteAsync(
            new SetRelationshipDescriptionCommand(_bodyPath, relationshipId, "Views accounts using"),
            TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(result.IsSuccess, result.Error);
        Assert.Contains("u -> web \"Views accounts using\" \"HTTPS\"", OnDisk(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SetRelationshipTechnology_SetsTheProtocol()
    {
        // Arrange.
        var relationshipId = _documents.WorkspaceOf(_bodyPath).Relationships.Single().Id;

        // Act.
        await _history.ExecuteAsync(
            new SetRelationshipTechnologyCommand(_bodyPath, relationshipId, "JSON/HTTPS"),
            TestContext.Current.CancellationToken);

        // Assert.
        Assert.Contains("u -> web \"Uses\" \"JSON/HTTPS\"", OnDisk(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnEditToSomethingAlreadyDeleted_IsRefusedRatherThanThrowing()
    {
        // Act.
        // An ordinary race: the canvas is a moment behind the document.
        var result = await _history.ExecuteAsync(new SetElementNameCommand(_bodyPath, "ghost", "Anything"), TestContext.Current.CancellationToken);

        // Assert.
        Assert.False(result.IsSuccess);
        Assert.Contains("ghost", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EveryEdit_LeavesTheDocumentParseable_AndTheOtherLinesUntouched()
    {
        // Arrange.
        var before = OnDisk();

        await _history.ExecuteAsync(new SetElementNameCommand(_bodyPath, "s", "Internet Banking"), TestContext.Current.CancellationToken);
        await _history.ExecuteAsync(new SetElementDescriptionCommand(_bodyPath, "web", "Delivers the app."), TestContext.Current.CancellationToken);

        // Act and assert, step by step.
        var after = OnDisk();
        Assert.Equal(2, DifferingLines(before, after));
        var workspace = C4Parser.Parse(C4Document.Parse(after));
        Assert.Equal("Internet Banking", workspace.Find("s")!.Name);
        Assert.Equal("Delivers the app.", workspace.Find("web")!.Description);
    }

    [Fact]
    public async Task UndoingBothEdits_RestoresTheOriginalBytes()
    {
        // Arrange.
        var before = OnDisk();
        await _history.ExecuteAsync(new SetElementNameCommand(_bodyPath, "s", "Internet Banking"), TestContext.Current.CancellationToken);
        await _history.ExecuteAsync(new SetElementDescriptionCommand(_bodyPath, "web", "Delivers the app."), TestContext.Current.CancellationToken);

        // Act.
        await _history.UndoAsync(TestContext.Current.CancellationToken);
        await _history.UndoAsync(TestContext.Current.CancellationToken);

        // Assert.
        Assert.Equal(before, OnDisk());
    }

    [Fact]
    public async Task AnEdit_ReachesTheSessionsOnThatDocument()
    {
        // Arrange.
        // The command edits the shared document, so every view of it hears about the change.
        var raised = 0;
        _documents.Changed += (_, _) => raised++;

        // Act.
        await _history.ExecuteAsync(new SetElementNameCommand(_bodyPath, "web", "Renamed"), TestContext.Current.CancellationToken);

        // Assert.
        Assert.Equal(1, raised);
    }

    [Fact]
    public async Task AWriteThatCannotLand_IsReportedRatherThanAnsweredWithSuccess()
    {
        // Arrange: a holder sharing Read only, which denies the replace a publish performs -
        // what an external editor with the file open looks like from here.
        using var holder = new FileStream(_bodyPath, FileMode.Open, FileAccess.Read, FileShare.Read);

        // Act.
        var result = await _history.ExecuteAsync(
            new SetElementNameCommand(_bodyPath, "web", "Web Application"),
            TestContext.Current.CancellationToken);

        // Assert: the caller is told. Answering success while the file still holds the old name
        // is worse than failing - the user believes the rename landed and it did not. This is
        // the WardleyDocumentStore defect, which C4 carried identically.
        Assert.False(result.IsSuccess);
        Assert.Contains("could not be written", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ADragWhoseLayoutCannotBeSaved_SucceedsButSaysSo()
    {
        // Arrange: the sidecar exists and is held so it cannot be replaced. A missing sidecar is
        // an empty layout and succeeds, so it has to exist for this to be the case under test.
        await _history.ExecuteAsync(
            new MoveC4ElementCommand(_bodyPath, "SystemContext", "web", 10d, 20d),
            TestContext.Current.CancellationToken);
        var sidecarPath = IoPath.Combine(_root, IoPath.GetFileNameWithoutExtension(_bodyPath) + ".layout.json");
        Assert.True(File.Exists(sidecarPath), $"expected a sidecar at {sidecarPath}");
        using var holder = new FileStream(sidecarPath, FileMode.Open, FileAccess.Read, FileShare.Read);

        // Act.
        var result = await _history.ExecuteAsync(
            new MoveC4ElementCommand(_bodyPath, "SystemContext", "web", 30d, 40d),
            TestContext.Current.CancellationToken);

        // Assert: the element DID move, so the command succeeds - refusing it would lose work the
        // user can see. But the position was not recorded, and the user is told rather than
        // finding out by reopening.
        Assert.True(result.IsSuccess, result.Error);
        Assert.NotEqual("", result.Warning);
        Assert.Contains("reopened", result.Warning, StringComparison.Ordinal);
    }
}
