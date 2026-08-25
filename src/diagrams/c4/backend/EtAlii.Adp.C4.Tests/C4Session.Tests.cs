using EtAlii.Adp.Backend;
using EtAlii.Adp.Backend.Diagrams;
using EtAlii.Adp.Diagram;
using Xunit;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.C4.Tests;

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

    public C4SessionTests()
    {
        _root = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.C4.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
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
        (C4Session)new C4SessionFactory(new DiagramOrigin("c4", "context"), _documents, _mapper)
            .Open(ShortGuid.NewShortGuid(), _root, bodyPath, registrationPath);

    private static IReadOnlyList<DiagramElement> Added(IReadOnlyList<DiagramDelta> deltas) =>
        deltas.OfType<DiagramDelta.Add>().SelectMany(add => add.Elements).ToArray();

    [Fact]
    public async Task ARegistrationNamingAView_OpensThatView()
    {
        var body = WriteModel();
        var adp = WriteRegistration("containers.adp", "c4/container", "model.dsl", "containers");

        await using var session = Open(body, adp);

        Assert.Equal("containers", session.View()!.Key);
        // The container view shows the container; the context view would not.
        Assert.Contains(Added(session.Baseline()), element => element.Id == "web");
    }

    [Fact]
    public async Task ADocumentOpenedWithoutARegistration_ShowsItsFirstView()
    {
        // Requirement 2.6: a .dsl dropped into the project on its own is still openable.
        var body = WriteModel();

        await using var session = Open(body, registrationPath: null);

        Assert.Equal("context", session.View()!.Key);
    }

    [Fact]
    public async Task ARegistrationNamingAViewTheDocumentDoesNotDeclare_ShowsNothing_RatherThanTheWrongOne()
    {
        var body = WriteModel();
        var adp = WriteRegistration("ghost.adp", "c4/context", "model.dsl", "ghost");

        await using var session = Open(body, adp);

        Assert.Null(session.View());
        Assert.Empty(Added(session.Baseline()));
    }

    [Fact]
    public async Task TheBaseline_CarriesTheViewsFurnitureAsWellAsItsElements()
    {
        var body = WriteModel();

        await using var session = Open(body, null);

        var elements = Added(session.Baseline());
        Assert.Contains(elements, element => element.Type == C4ElementMapper.NodeType);
        Assert.Contains(elements, element => element.Type == C4ElementMapper.ViewType);
    }

    [Fact]
    public async Task AnEditThroughOneView_ReachesASessionOnAnotherViewOfTheSameModel()
    {
        // The premise of the whole spec: one model, several views, and a rename in one is a
        // rename in all of them because there is one element and not several.
        var body = WriteModel();
        var contextAdp = WriteRegistration("context.adp", "c4/context", "model.dsl", "context");
        var containersAdp = WriteRegistration("containers.adp", "c4/container", "model.dsl", "containers");

        await using var context = Open(body, contextAdp);
        await using var containers = Open(body, containersAdp);
        _ = context.Baseline();
        _ = containers.Baseline();

        DiagramDeltasEventArgs? pushedToContext = null;
        context.Changed += (_, args) => pushedToContext = args;

        // Edit through the document the containers view holds, and save.
        var document = _documents.GetOrLoad(body);
        var line = document.CodeLines.First(l => l.Code.Contains("softwareSystem", StringComparison.Ordinal));
        document.ReplaceLine(line.Number, line.Text.Replace("\"Banking\"", "\"Renamed\"", StringComparison.Ordinal));
        _documents.Save(body);

        Assert.NotNull(pushedToContext);
        var renamed = Added(pushedToContext!.Deltas).Single(element => element.Id == "s");
        Assert.Equal("Renamed", C4ElementPayload.Parser.ParseFrom(renamed.Payload.Span).Name);
    }

    [Fact]
    public async Task UpdateView_NarrowingToNothing_RemovesWhatLeft()
    {
        var body = WriteModel();
        await using var session = Open(body, null);
        _ = session.Baseline();

        var deltas = session.UpdateView(new DiagramViewport(100000, 100000, 200000, 200000));

        var removed = deltas.OfType<DiagramDelta.Remove>().SelectMany(remove => remove.ElementIds).ToArray();
        Assert.Contains("u", removed);
        Assert.Contains("s", removed);
    }

    [Fact]
    public async Task UpdateView_WideningAgain_BringsThemBack()
    {
        var body = WriteModel();
        await using var session = Open(body, null);
        _ = session.Baseline();
        _ = session.UpdateView(new DiagramViewport(100000, 100000, 200000, 200000));

        var deltas = session.UpdateView(DiagramViewport.Unbounded);

        Assert.Contains(Added(deltas), element => element.Id == "s");
        Assert.Empty(deltas.OfType<DiagramDelta.Remove>().SelectMany(remove => remove.ElementIds));
    }

    [Fact]
    public async Task ADocumentThatDoesNotExistYet_OpensEmpty_RatherThanFailing()
    {
        await using var session = Open(IoPath.Combine(_root, "not-yet.dsl"), null);

        Assert.Null(session.View());
        Assert.Empty(session.Baseline());
    }

    [Fact]
    public async Task DisposingASession_StopsItListeningToTheDocument()
    {
        var body = WriteModel();
        var session = Open(body, null);
        _ = session.Baseline();

        var pushes = 0;
        session.Changed += (_, _) => pushes++;
        await session.DisposeAsync();

        _documents.Save(body);

        Assert.Equal(0, pushes);
    }
}
