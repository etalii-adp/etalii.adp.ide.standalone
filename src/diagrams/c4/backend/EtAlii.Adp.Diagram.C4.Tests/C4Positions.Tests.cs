using System.Text.Json;
using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.History;
using EtAlii.Adp.Sessions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Diagram.C4.Tests;

/// <summary>
/// Where a C4 position is kept and how it is undone (c4-disl-fbl task 16, Requirement 7): an existing
/// <c>*.layout.json</c> is read and written while it exists, by splices, and none is created (Q3, FBL 8.7).
/// </summary>
public sealed class C4PositionsTests : IDisposable
{
    private const string Model = """
        workspace "Bank" {
            model {
                a = softwareSystem "A" "desc"
                b = softwareSystem "B" "desc"
            }
            views {
                systemLandscape "all" {
                    include *
                }
                systemLandscape "other" {
                    include *
                }
            }
        }
        """;

    private const string Sidecar = "{\n  \"other\": {\n    \"a\": { \"x\": 40, \"y\": 60 }\n  }\n}\n";

    private readonly TestFolder _testFolder = TestFolder.Create("c4-positions");
    private readonly string _bodyPath;
    private readonly string _sidecarPath;
    private readonly ServiceProvider _services;
    private readonly IHistoryStack _history;

    public C4PositionsTests()
    {
        _bodyPath = IoPath.Combine(_testFolder.Path, "model.dsl");
        _sidecarPath = IoPath.Combine(_testFolder.Path, "model.layout.json");
        File.WriteAllText(_bodyPath, Model);
        File.WriteAllText(_sidecarPath, Sidecar);
        _services = new ServiceCollection().AddCommands().AddHierarchyCommandHandlers().AddC4().BuildServiceProvider();
        _history = _services.GetRequiredService<IHistoryStackStore>().Get(ProjectRoot.From(_testFolder.Path));
    }

    public void Dispose()
    {
        _services.Dispose();
        _testFolder.Dispose();
    }

    [Fact]
    public async Task UndoingAFirstPlacement_RemovesThatViewsEntryOnly()
    {
        // Act: a is placed in "all" for the first time, and the placement undone.
        var moved = await _history.ExecuteAsync(new MoveC4ElementCommand(_bodyPath, "all", "a", 100, 200), TestContext.Current.CancellationToken);
        Assert.True(moved.IsSuccess, moved.Error);
        Assert.Equal(100, Position(await File.ReadAllTextAsync(_sidecarPath, TestContext.Current.CancellationToken), "all", "a"));
        await _history.UndoAsync(TestContext.Current.CancellationToken);

        // Assert: a has no entry in "all" any more, and keeps the one it had in "other" (finding B17).
        var after = await File.ReadAllTextAsync(_sidecarPath, TestContext.Current.CancellationToken);
        Assert.Null(Position(after, "all", "a"));
        Assert.Equal(40, Position(after, "other", "a"));
    }

    [Fact]
    public async Task APositionThatCannotBeStored_LeavesTheMoveInPlace_AndTellsTheProject()
    {
        // Arrange: a body with no layout file and no registration, so FBL has nowhere to keep a position (Q3), and a
        // project whose notices are recorded as the host's ContextNoticeSink would pass them to every watcher.
        var folder = IoPath.Combine(_testFolder.Path, "unregistered");
        Directory.CreateDirectory(folder);
        var bodyPath = IoPath.Combine(folder, "model.dsl");
        await File.WriteAllTextAsync(bodyPath, Model, TestContext.Current.CancellationToken);
        var notices = new RecordingNotices();
        await using var services = new ServiceCollection().AddSingleton<ICommandNoticeSink>(notices).AddCommands().AddHierarchyCommandHandlers().AddC4().BuildServiceProvider();
        var root = ProjectRoot.From(folder);
        await using var session = (C4Session)services
            .GetServices<IDiagramSessionFactory>()
            .First(factory => factory.Origin.Key == "c4/system-landscape")
            .Open(ShortGuid.NewShortGuid(), root, bodyPath, null);

        // Act.
        var refusal = await session.MoveElementToAsync("a", 100, 200, TestContext.Current.CancellationToken);

        // Assert: the drag is not refused, nothing is created beside the body, and today's sentence reaches the project.
        Assert.Equal("", refusal);
        Assert.Equal(["model.dsl"], Directory.GetFiles(folder).Select(IoPath.GetFileName));
        (ProjectRoot notified, string message) = Assert.Single(notices.Notified);
        Assert.Equal(root, notified);
        Assert.Equal("The new position could not be saved beside model.dsl, so it will not be there when the diagram is reopened.", message);
    }

    [Fact]
    public async Task AnUnreadableLayoutFile_LeavesTheMoveInPlace_AndTellsTheProject()
    {
        // Arrange: the layout file beside the body is not JSON, so the position cannot be placed in it.
        await File.WriteAllTextAsync(_sidecarPath, "{ not json", TestContext.Current.CancellationToken);
        var notices = new RecordingNotices();
        await using var services = new ServiceCollection().AddSingleton<ICommandNoticeSink>(notices).AddCommands().AddHierarchyCommandHandlers().AddC4().BuildServiceProvider();
        var root = ProjectRoot.From(_testFolder.Path);
        await using var session = (C4Session)services
            .GetServices<IDiagramSessionFactory>()
            .First(factory => factory.Origin.Key == "c4/system-landscape")
            .Open(ShortGuid.NewShortGuid(), root, _bodyPath, null);

        // Act.
        var refusal = await session.MoveElementToAsync("a", 100, 200, TestContext.Current.CancellationToken);

        // Assert: the drag is not refused, the file is left as it was, and the second of today's two sentences reaches the project.
        Assert.Equal("", refusal);
        Assert.Equal("{ not json", await File.ReadAllTextAsync(_sidecarPath, TestContext.Current.CancellationToken));
        (ProjectRoot notified, string message) = Assert.Single(notices.Notified);
        Assert.Equal(root, notified);
        Assert.Equal("The layout beside model.dsl could not be updated, so positions may not be as expected when the diagram is reopened.", message);
    }

    [Fact]
    public async Task AnEntryUnderAnIdTheBodyDoesNotStore_IsReported()
    {
        // Arrange: "other" keeps a position for a, which the body stores, and one for gone, which it does not.
        await File.WriteAllTextAsync(_sidecarPath, "{\n  \"other\": {\n    \"a\": { \"x\": 40, \"y\": 60 },\n    \"gone\": { \"x\": 1, \"y\": 2 }\n  }\n}\n", TestContext.Current.CancellationToken);
        var validator = new C4Validator(Diagram.SystemLandscape.Origin);

        // Act.
        var problems = await validator.ValidateAsync(new DiagramValidationRequest(Model, "model", ProjectRoot.From(_testFolder.Path), _bodyPath, null), TestContext.Current.CancellationToken);

        // Assert: the stale entry, which the next placement in "other" drops (Q7), is told; the stored one is not.
        var stale = Assert.Single(problems, problem => problem.RuleId == "fbl.stale-view-data");
        Assert.Equal("View 'other': The layout file keeps a position for 'gone', which the file no longer has; it is removed at the next placement in this view.", stale.Message);
        Assert.Equal(DiagramProblemSeverity.Warning, stale.Severity);
    }

    /// <summary>The notices the history stack sends, as the host's sink receives them.</summary>
    private sealed class RecordingNotices : ICommandNoticeSink
    {
        public List<(ProjectRoot Root, string Message)> Notified { get; } = [];

        public void Notify(ProjectRoot root, string message) => Notified.Add((root, message));
    }

    /// <summary>The x the layout file holds for <paramref name="element"/> in <paramref name="view"/>, or null.</summary>
    private static double? Position(string json, string view, string element)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.TryGetProperty(view, out var positions) && positions.TryGetProperty(element, out var position)
            ? position.GetProperty("x").GetDouble()
            : null;
    }
}
