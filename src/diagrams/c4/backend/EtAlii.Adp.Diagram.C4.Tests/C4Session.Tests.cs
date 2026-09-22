using EtAlii.Adp.Common;
using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.History;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Diagram.C4.Tests;

/// <summary>
/// One diagram, one connection - and the premise the whole spec rests on: two views over one
/// document stay in step, because they are two views of one model rather than two drawings
/// (c4-diagrams Requirements 1.2, 1.5, 2.4, 2.6).
/// </summary>
public class C4SessionTests : IDisposable
{
    private readonly string _root;
    private readonly C4DocumentStore _documents = new();
    private readonly C4ElementMapper _mapper = new(C4Metrics.Default, new C4LayoutSidecar());
    private readonly IHistoryStackStore _historyStacks = new ServiceCollection().AddCommands().AddHierarchyCommandHandlers().AddC4().BuildServiceProvider().GetRequiredService<IHistoryStackStore>();

    public C4SessionTests()
    {
        _root = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.Diagram.C4.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        TestFolder.TryDelete(_root);
    }

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
                container s "containers" {
                    include *
                }
            }
        }
        """;

    private string WriteModel(string name = "model.dsl", string text = Model)
    {
        var path = IoPath.Combine(_root, name);
        File.WriteAllText(path, text);
        return path;
    }

    private string WriteRegistration(string name, string mime, string body, string? view)
    {
        var path = IoPath.Combine(_root, name);
        File.WriteAllText(path, mime + "\nbody: " + body + "\n" + (view is null ? "" : "view: " + view + "\n"));
        return path;
    }

    private C4Session Open(string bodyPath, string? registrationPath) =>
        (C4Session)new C4SessionFactory(new DiagramOrigin("c4", "context"), _documents, _mapper, _historyStacks)
            .Open(ShortGuid.NewShortGuid(), _root, bodyPath, registrationPath);

    private static IReadOnlyList<DiagramElement> Added(IReadOnlyList<DiagramDelta> deltas) =>
        deltas.OfType<DiagramAddDelta>().SelectMany(add => add.Elements).ToArray();

    [Fact]
    public async Task ARegistrationNamingAView_OpensThatView()
    {
        // Arrange.
        var body = WriteModel();
        var adp = WriteRegistration("containers.adp", "c4/container", "model.dsl", "containers");

        // Act.
        await using var session = Open(body, adp);

        // Assert.
        Assert.Equal("containers", session.View()!.Key);
        // The container view shows the container; the context view would not.
        Assert.Contains(Added(session.Baseline()), element => element.Id == "web");
    }

    [Fact]
    public async Task ADocumentOpenedWithoutARegistration_ShowsItsFirstView()
    {
        // Arrange.
        // Requirement 2.6: a .dsl dropped into the project on its own is still openable.
        var body = WriteModel();

        // Act.
        await using var session = Open(body, registrationPath: null);

        // Assert.
        Assert.Equal("context", session.View()!.Key);
    }

    [Fact]
    public async Task ARegistrationNamingAViewTheDocumentDoesNotDeclare_ShowsNothing_RatherThanTheWrongOne()
    {
        // Arrange.
        var body = WriteModel();
        var adp = WriteRegistration("ghost.adp", "c4/context", "model.dsl", "ghost");

        // Act.
        await using var session = Open(body, adp);

        // Assert.
        Assert.Null(session.View());
        Assert.Empty(Added(session.Baseline()));
    }

    [Fact]
    public async Task TheBaseline_CarriesTheViewsFurnitureAsWellAsItsElements()
    {
        // Arrange.
        var body = WriteModel();

        await using var session = Open(body, null);

        // Act and assert, step by step.
        var elements = Added(session.Baseline());
        Assert.Contains(elements, element => element.Type == C4ElementMapper.NodeType);
        Assert.Contains(elements, element => element.Type == C4ElementMapper.ViewType);
    }

    [Fact]
    public async Task AnEditThroughOneView_ReachesASessionOnAnotherViewOfTheSameModel()
    {
        // Arrange.
        // The premise of the whole spec: one model, several views, and a rename in one is a
        // rename in all of them because there is one element and not several.
        var body = WriteModel();
        var contextAdp = WriteRegistration("context.adp", "c4/context", "model.dsl", "context");
        var containersAdp = WriteRegistration("containers.adp", "c4/container", "model.dsl", "containers");

        // Arrange, continued.
        await using var context = Open(body, contextAdp);
        await using var containers = Open(body, containersAdp);
        _ = context.Baseline();
        _ = containers.Baseline();

        // Arrange, continued.
        DiagramDeltasEventArgs? pushedToContext = null;
        context.Changed += (_, args) => pushedToContext = args;

        // Act.
        // Edit through the document the containers view holds, and save.
        var document = _documents.GetOrLoad(body);
        var line = document.CodeLines.First(l => l.Code.Contains("softwareSystem", StringComparison.Ordinal));
        document.ReplaceLine(line.Number, line.Text.Replace("\"Banking\"", "\"Renamed\"", StringComparison.Ordinal));
        _documents.Save(body);

        // Assert.
        Assert.NotNull(pushedToContext);
        var renamed = Added(pushedToContext!.Deltas).Single(element => element.Id == "s");
        Assert.Equal("Renamed", C4ElementPayload.Parser.ParseFrom(renamed.Payload.Span).Name);
    }

    [Fact]
    public async Task ADocumentChange_ThatDeletesAnElement_RemovesItFromTheCanvas()
    {
        // Arrange.
        var body = WriteModel();
        await using var session = Open(body, null);
        Assert.Contains(Added(session.Baseline()), element => element.Id == "u");

        IReadOnlyList<DiagramDelta>? pushed = null;
        session.Changed += (_, args) => pushed = args.Deltas;

        // Act: the person and its relationship are deleted in a text editor, and the watcher
        // reports the change. The client folds an add as an upsert and removes only on a remove,
        // so nothing but a remove delta takes the person off the canvas.
        var kept = Model.Split('\n').Where(line => !line.Contains("u = person", StringComparison.Ordinal) && !line.Contains("u -> web", StringComparison.Ordinal));
        await File.WriteAllTextAsync(body, string.Join('\n', kept), TestContext.Current.CancellationToken);
        Assert.DoesNotContain("person", await File.ReadAllTextAsync(body, TestContext.Current.CancellationToken), StringComparison.Ordinal);
        _documents.Reload(body);

        // Assert.
        Assert.NotNull(pushed);
        var removed = pushed!.OfType<DiagramRemoveDelta>().SelectMany(remove => remove.ElementIds).ToArray();
        Assert.Contains("u", removed);
    }

    [Fact]
    public async Task UpdateView_NarrowingToNothing_RemovesWhatLeft()
    {
        // Arrange.
        var body = WriteModel();
        await using var session = Open(body, null);
        _ = session.Baseline();

        var deltas = session.UpdateView(new DiagramViewport(100000, 100000, 200000, 200000));

        // Act and assert, step by step.
        var removed = deltas.OfType<DiagramRemoveDelta>().SelectMany(remove => remove.ElementIds).ToArray();
        Assert.Contains("u", removed);
        Assert.Contains("s", removed);
    }

    [Fact]
    public async Task UpdateView_WideningAgain_BringsThemBack()
    {
        // Arrange.
        var body = WriteModel();
        await using var session = Open(body, null);
        _ = session.Baseline();
        _ = session.UpdateView(new DiagramViewport(100000, 100000, 200000, 200000));

        // Act.
        var deltas = session.UpdateView(DiagramViewport.Unbounded);

        // Assert.
        Assert.Contains(Added(deltas), element => element.Id == "s");
        Assert.Empty(deltas.OfType<DiagramRemoveDelta>().SelectMany(remove => remove.ElementIds));
    }

    [Fact]
    public async Task ADocumentThatDoesNotExistYet_OpensEmpty_RatherThanFailing()
    {
        // Act.
        await using var session = Open(IoPath.Combine(_root, "not-yet.dsl"), null);

        // Assert.
        Assert.Null(session.View());
        Assert.Empty(session.Baseline());
    }

    [Fact]
    public async Task DisposingASession_StopsItListeningToTheDocument()
    {
        // Arrange.
        var body = WriteModel();
        var session = Open(body, null);
        _ = session.Baseline();

        // Arrange, continued.
        var pushes = 0;
        session.Changed += (_, _) => pushes++;
        await session.DisposeAsync();

        // Act.
        _documents.Save(body);

        // Assert.
        Assert.Equal(0, pushes);
    }
}
