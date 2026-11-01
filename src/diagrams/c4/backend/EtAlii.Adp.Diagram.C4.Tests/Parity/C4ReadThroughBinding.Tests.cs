using EtAlii.Adp.Files;
using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.History;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Diagram.C4.Tests.Parity;

/// <summary>
/// Several readings of one <c>.dsl</c> read through one open body (c4-disl-fbl task 14, Requirements 6.1 to 6.4):
/// what <see cref="C4Body"/> holds that the session and store tests beside it do not. A <c>view:</c> naming no view
/// opening an empty canvas is held already by <c>C4SessionTests.ARegistrationNamingAViewTheDocumentDoesNotDeclare_ShowsNothing_RatherThanTheWrongOne</c>.
/// </summary>
public class C4ReadThroughBindingTests : IDisposable
{
    private readonly TestFolder _testFolder = TestFolder.Create("c4-read-through-binding");
    private readonly string _root;
    private readonly IC4DocumentStore _documents;
    private readonly C4ElementMapper _mapper = new(C4Metrics.Default, new C4LayoutSidecar(new FileWriter()));
    private readonly ServiceProvider _services;
    private readonly IHistoryStackStore _historyStacks;

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

    public C4ReadThroughBindingTests()
    {
        _root = _testFolder.Path;
        _services = new ServiceCollection().AddCommands().AddHierarchyCommandHandlers().AddC4().BuildServiceProvider();
        _historyStacks = _services.GetRequiredService<IHistoryStackStore>();
        _documents = _services.GetRequiredService<IC4DocumentStore>();
    }

    public void Dispose()
    {
        _services.Dispose();
        _testFolder.Dispose();
    }

    [Fact]
    public async Task TwoReadingsOfOneDsl_ShareOneBody_AndAnEditThroughTheHistoryReachesBoth()
    {
        // Arrange.
        var body = await WriteAsync("model.dsl", Model);
        var context = await WriteAsync("context.adp", "c4/context\nbody: model.dsl\nview: context\n");
        var containers = await WriteAsync("containers.adp", "c4/container\nbody: model.dsl\nview: containers\n");
        await using var first = Open(new ToolOrigin("c4", "context"), body, context);
        await using var second = Open(new ToolOrigin("c4", "container"), body, containers);
        _ = await first.BaselineAsync(TestContext.Current.CancellationToken);
        _ = await second.BaselineAsync(TestContext.Current.CancellationToken);
        var pushed = 0;
        first.Changed += (_, _) => pushed++;

        // Act.
        // One edit, through the one history every reading of the project shares.
        var result = await _historyStacks.Get(ProjectRoot.From(_root)).ExecuteAsync(
            new SetElementDescriptionCommand(body, "s", "Moves money."), TestContext.Current.CancellationToken);

        // Assert.
        // Both readings drew the one model of the one open body, and the edit reached the reading it was not made through.
        Assert.True(result.IsSuccess, result.Error);
        var after = (await _documents.GetOrLoadAsync(body, TestContext.Current.CancellationToken)).Model;
        Assert.Equal("Moves money.", after.Find("s")!.Description);
        Assert.Same(after, C4Body.Of(after).Workspace);
        Assert.Equal("containers", (await second.ViewAsync(TestContext.Current.CancellationToken))!.Key);
        Assert.True(pushed > 0);
    }

    [Fact]
    public async Task OneReadingOfOneBody_BuildsItsModelOnce_ForEveryMenuAndRowAskedOfIt()
    {
        // Arrange.
        var file = await WriteAsync("model.dsl", Model);
        var workspace = C4Body.Read(Model).Workspace;
        var view = workspace.FindView("containers");
        var specification = C4Definitions.For(C4ViewKind.Container).Specification;

        // Act.
        var once = C4Body.Of(workspace, view, specification, C4Body.FileOf(file));
        var again = C4Body.Of(workspace, view, specification, C4Body.FileOf(file));

        // Assert.
        // The reading's model is the body's, not one built afresh for each question a menu or a row asks.
        Assert.Same(once, again);
        Assert.Same(C4Body.Read(Model), C4Body.Of(workspace));
    }

    [Fact]
    public async Task AContextRegistrationNamingAContainerView_ShowsThatView_DrawnByTheContainerDefinition()
    {
        // Arrange.
        // Q6, as today: the named view whatever its kind, drawn by the definition of the view's own kind.
        var body = await WriteAsync("model.dsl", Model);
        var registration = await WriteAsync("containers.adp", "c4/context\nbody: model.dsl\nview: CONTAINERS\n");
        await using var session = Open(new ToolOrigin("c4", "context"), body, registration);

        // Act.
        var shown = await session.ViewAsync(TestContext.Current.CancellationToken);
        var workspace = (await _documents.GetOrLoadAsync(body, TestContext.Current.CancellationToken)).Model;
        var file = C4Body.FileOf(body);
        // Only the container definition's toolbox has a container to drop into a software system.
        var onTheShownView = C4Definitions.Drop(workspace, file, shown, new ToolOrigin("c4", "context"), "s", AddContainer);
        var onTheContextView = C4Definitions.Drop(workspace, file, workspace.FindView("context"), new ToolOrigin("c4", "context"), "s", AddContainer);

        // Assert.
        Assert.NotNull(shown);
        Assert.Equal("containers", shown.Key);
        Assert.Equal(C4ViewKind.Container, shown.Kind);
        Assert.NotNull(onTheShownView);
        Assert.Null(onTheContextView);
    }

    [Fact]
    public async Task ARegistrationWithoutABody_ReadsTheDslOfItsOwnBaseName()
    {
        // Arrange.
        var body = await WriteAsync("banking.dsl", Model);
        var registration = await WriteAsync("banking.adp", "c4/container\nview: containers\n");
        var router = new DiagramFileRouter(new C4ExamplesStubCatalog());

        // Act.
        var routed = Assert.IsType<DiagramRouted>(router.Route(registration, _root));
        await using var session = Open(new ToolOrigin("c4", "container"), routed.BodyPath!, registration);
        var shown = await session.ViewAsync(TestContext.Current.CancellationToken);

        // Assert.
        Assert.Equal(IoPath.GetFullPath(body), IoPath.GetFullPath(routed.BodyPath!));
        Assert.NotNull(shown);
        Assert.Equal("containers", shown.Key);
        Assert.Equal("s", shown.ScopeId);
    }

    private async Task<string> WriteAsync(string name, string text)
    {
        var path = IoPath.Combine(_root, name);
        await File.WriteAllTextAsync(path, text, TestContext.Current.CancellationToken);
        return path;
    }

    private C4Session Open(ToolOrigin origin, string body, string? registration) =>
        (C4Session)new C4SessionFactory(origin, _documents, _mapper, _historyStacks)
            .Open(ShortGuid.NewShortGuid(), ProjectRoot.From(_root), body, registration);

    /// <summary>The drop of the container definition's container tool.</summary>
    private const string AddContainer = "c4.add-container";
}
