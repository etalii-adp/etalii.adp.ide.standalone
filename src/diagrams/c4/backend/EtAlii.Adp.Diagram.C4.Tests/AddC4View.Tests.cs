using EtAlii.Adp.Backend;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Diagram.C4.Tests;

/// <summary>
/// Adding a second view to a model that already exists - the action that turns one C4 diagram
/// into the several views of one model C4 is built around (c4-diagrams Requirement 2.7).
/// </summary>
public class AddC4ViewTests : IDisposable
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
                systemContext s "context" {
                    include *
                }
            }
        }
        """;

    public AddC4ViewTests()
    {
        _root = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.Diagram.C4.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _bodyPath = IoPath.Combine(_root, "banking.dsl");
        File.WriteAllText(_bodyPath, Model);

        _services = new ServiceCollection().AddCommands().AddC4().BuildServiceProvider();
        _documents = _services.GetRequiredService<IC4DocumentStore>();
        _history = _services.GetRequiredService<IHistoryStackStore>().Get(_root);
    }

    public void Dispose()
    {
        _services.Dispose();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private AddC4ViewCommand AddContainers(string key = "containers") => new(
        _bodyPath,
        IoPath.Combine(_root, key + ".adp"),
        "c4/container",
        C4ViewKind.Container,
        key,
        "banking.dsl");

    [Fact]
    public async Task AddingAView_AppendsItToTheDocument_AndCreatesTheRegistrationThatOpensIt()
    {
        // Arrange.
        var result = await _history.ExecuteAsync(AddContainers(), TestContext.Current.CancellationToken);

        // Act and assert, step by step.
        Assert.True(result.IsSuccess, result.Error);

        // The view is in the document...
        var workspace = _documents.WorkspaceOf(_bodyPath);
        var view = workspace.FindView("containers");
        Assert.NotNull(view);
        Assert.Equal(C4ViewKind.Container, view.Kind);
        Assert.Equal("s", view.ScopeId);

        // ...and the .adp names that body and that view.
        var registration = await File.ReadAllTextAsync(IoPath.Combine(_root, "containers.adp"), TestContext.Current.CancellationToken);
        Assert.StartsWith("c4/container\n", registration, StringComparison.Ordinal);
        Assert.Contains("body: banking.dsl", registration, StringComparison.Ordinal);
        Assert.Contains("view: containers", registration, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AddingAView_LeavesTheRestOfTheDocumentExactlyAsItWas()
    {
        // Arrange.
        var before = await File.ReadAllLinesAsync(_bodyPath, TestContext.Current.CancellationToken);

        await _history.ExecuteAsync(AddContainers(), TestContext.Current.CancellationToken);

        // Act and assert, step by step.
        var after = await File.ReadAllLinesAsync(_bodyPath, TestContext.Current.CancellationToken);
        // Only the appended block is new; every line that was there is still there, in order.
        Assert.Equal(before.Length + 4, after.Length);
        Assert.Equal(before.Take(10), after.Take(10));
    }

    [Fact]
    public async Task TheExistingViewStillOpens_AfterASecondIsAdded()
    {
        // Arrange.
        await _history.ExecuteAsync(AddContainers(), TestContext.Current.CancellationToken);

        // Act and assert, step by step.
        var workspace = _documents.WorkspaceOf(_bodyPath);
        Assert.NotNull(workspace.FindView("context"));
        Assert.Equal(2, workspace.Views.Count);
    }

    [Fact]
    public async Task AddingAView_Undoes_RemovingBothTheBlockAndTheRegistration()
    {
        // Arrange.
        var before = await File.ReadAllTextAsync(_bodyPath, TestContext.Current.CancellationToken);
        await _history.ExecuteAsync(AddContainers(), TestContext.Current.CancellationToken);

        // Act.
        await _history.UndoAsync(TestContext.Current.CancellationToken);

        // Assert.
        Assert.Equal(before, await File.ReadAllTextAsync(_bodyPath, TestContext.Current.CancellationToken));
        Assert.False(File.Exists(IoPath.Combine(_root, "containers.adp")));
    }

    [Fact]
    public async Task AddingAViewTwiceUnderOneKey_IsRefused()
    {
        // Arrange.
        await _history.ExecuteAsync(AddContainers(), TestContext.Current.CancellationToken);

        // Act.
        var result = await _history.ExecuteAsync(AddContainers(), TestContext.Current.CancellationToken);

        // Assert.
        Assert.False(result.IsSuccess);
        Assert.Contains("already has a view", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ALandscapeView_IsAddedWithNoScope()
    {
        // Arrange.
        // A landscape is a context diagram without a focus, so it takes no scope element.
        var command = new AddC4ViewCommand(
            _bodyPath, IoPath.Combine(_root, "landscape.adp"), "c4/system-landscape",
            C4ViewKind.SystemLandscape, "landscape", "banking.dsl");

        // Act.
        var result = await _history.ExecuteAsync(command, TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(result.IsSuccess, result.Error);
        var view = _documents.WorkspaceOf(_bodyPath).FindView("landscape")!;
        Assert.Equal(C4ViewKind.SystemLandscape, view.Kind);
        Assert.Null(view.ScopeId);
    }

    [Fact]
    public async Task AComponentView_IsScopedToAContainer_NotASystem()
    {
        // Arrange.
        var command = new AddC4ViewCommand(
            _bodyPath, IoPath.Combine(_root, "components.adp"), "c4/component",
            C4ViewKind.Component, "components", "banking.dsl");

        // Act.
        await _history.ExecuteAsync(command, TestContext.Current.CancellationToken);

        // Assert.
        Assert.Equal("web", _documents.WorkspaceOf(_bodyPath).FindView("components")!.ScopeId);
    }

    [Fact]
    public async Task ADynamicView_IsAddedWithNoIncludeAll()
    {
        // Arrange: "include *" means something different on a dynamic view: its body is its ordered
        // interactions, and a new one has none.
        var command = new AddC4ViewCommand(
            _bodyPath, IoPath.Combine(_root, "scenario.adp"), "c4/dynamic",
            C4ViewKind.Dynamic, "scenario", "banking.dsl");

        // Act.
        await _history.ExecuteAsync(command, TestContext.Current.CancellationToken);

        // Assert.
        var document = await File.ReadAllTextAsync(_bodyPath, TestContext.Current.CancellationToken);
        var block = document[document.IndexOf("dynamic s \"scenario\"", StringComparison.Ordinal)..];
        Assert.DoesNotContain("include *", block[..block.IndexOf('}')], StringComparison.Ordinal);
    }

    [Fact]
    public async Task AddingAViewToAModelWithNoSystem_IsRefusedRatherThanWritingSomethingBroken()
    {
        // Arrange.
        var empty = IoPath.Combine(_root, "empty.dsl");
        await File.WriteAllTextAsync(empty, "workspace {\n    model {\n    }\n    views {\n    }\n}\n", TestContext.Current.CancellationToken);

        // Act.
        var command = new AddC4ViewCommand(
            empty, IoPath.Combine(_root, "x.adp"), "c4/container", C4ViewKind.Container, "containers", "empty.dsl");
        var result = await _history.ExecuteAsync(command, TestContext.Current.CancellationToken);

        // Assert.
        Assert.False(result.IsSuccess);
        Assert.Contains("no software system", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(IoPath.Combine(_root, "x.adp")));
    }

    [Fact]
    public async Task TheAddedDocumentStillParses_AndTheNewViewLaysOut()
    {
        // Arrange.
        await _history.ExecuteAsync(AddContainers(), TestContext.Current.CancellationToken);

        // Act.
        var workspace = C4Parser.Parse(C4Document.Parse(await File.ReadAllTextAsync(_bodyPath, TestContext.Current.CancellationToken)));
        var view = workspace.FindView("containers")!;
        var layout = C4LayoutEngine.Compute(workspace, view, C4Metrics.Default);

        // Assert.
        Assert.Contains("web", layout.Boxes.Keys);
        Assert.Single(layout.Boundaries);
    }
}
