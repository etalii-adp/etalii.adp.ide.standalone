using EtAlii.Adp.Common;
using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.History;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Diagram.C4.Tests;

/// <summary>
/// Dragging an element on a C4 diagram. It places the element; it does not re-parent it -
/// containment is what the model says, and dropping a container onto another system would be a
/// claim about the architecture rather than an arrangement (c4-diagrams Requirement 8.3).
/// </summary>
public class C4MoveElementTests : IDisposable
{
    private readonly string _root;
    private readonly string _bodyPath;
    private readonly ServiceProvider _services;
    private readonly IC4DocumentStore _documents;
    private readonly C4LayoutSidecar _sidecar;
    private readonly IHistoryStack _history;

    private const string Model = """
        workspace "Bank" {
            model {
                a = softwareSystem "A" "desc"
                b = softwareSystem "B" "desc"
                a -> b "Calls" "HTTPS"
            }
            views {
                systemLandscape "all" {
                    include *
                }
            }
        }
        """;

    public C4MoveElementTests()
    {
        _root = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.Diagram.C4.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _bodyPath = IoPath.Combine(_root, "model.dsl");
        File.WriteAllText(_bodyPath, Model);

        _services = new ServiceCollection().AddCommands().AddHierarchyCommandHandlers().AddC4().BuildServiceProvider();
        _documents = _services.GetRequiredService<IC4DocumentStore>();
        _sidecar = _services.GetRequiredService<C4LayoutSidecar>();
        _history = _services.GetRequiredService<IHistoryStackStore>().Get(_root);
    }

    public void Dispose()
    {
        _services.Dispose();
        TestFolder.TryDelete(_root);
    }

    private C4Session Open() => (C4Session)_services
        .GetServices<IDiagramSessionFactory>()
        .First(factory => factory.Origin.Key == "c4/system-landscape")
        .Open(ShortGuid.NewShortGuid(), _root, _bodyPath, null);

    [Fact]
    public async Task ADrag_RecordsThePositionInTheSidecar_AndLeavesTheDocumentAlone()
    {
        // Arrange.
        var before = await File.ReadAllTextAsync(_bodyPath, TestContext.Current.CancellationToken);
        await using var session = Open();

        // Act.
        var error = await session.MoveElementToAsync("a", 250, 400, TestContext.Current.CancellationToken);

        // Assert.
        Assert.Equal("", error);
        Assert.Equal(new C4SidecarPosition(250, 400), _sidecar.Read(_bodyPath, "all")["a"]);
        // The model did not change: a position is view state, and the .dsl is another
        // ecosystem's file (Requirement 3.5).
        Assert.Equal(before, await File.ReadAllTextAsync(_bodyPath, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ADrag_IsOneUndoAway()
    {
        // Arrange.
        await using var session = Open();
        await session.MoveElementToAsync("a", 250, 400, TestContext.Current.CancellationToken);

        // Act.
        await _history.UndoAsync(TestContext.Current.CancellationToken);

        // Assert.
        // Undoing the first drag of an element hands it back to the layout rather than pinning
        // it wherever it happened to have been computed.
        Assert.DoesNotContain("a", _sidecar.Read(_bodyPath, "all").Keys);
    }

    [Fact]
    public async Task UndoingASecondDrag_RestoresTheFirstPosition()
    {
        // Arrange.
        await using var session = Open();
        await session.MoveElementToAsync("a", 100, 100, TestContext.Current.CancellationToken);
        await session.MoveElementToAsync("a", 250, 400, TestContext.Current.CancellationToken);

        // Act.
        await _history.UndoAsync(TestContext.Current.CancellationToken);

        // Assert.
        Assert.Equal(new C4SidecarPosition(100, 100), _sidecar.Read(_bodyPath, "all")["a"]);
    }

    [Fact]
    public async Task ADrag_TellsTheSessionsSoTheOtherTabRedraws()
    {
        // Arrange.
        await using var session = Open();
        var pushes = 0;
        session.Changed += (_, _) => pushes++;

        // Act.
        await session.MoveElementToAsync("a", 250, 400, TestContext.Current.CancellationToken);

        // Assert.
        Assert.Equal(1, pushes);
    }

    [Fact]
    public async Task ADroppedPositionIsHonouredByTheNextLayout()
    {
        // Arrange.
        await using var session = Open();
        await session.MoveElementToAsync("a", 250, 400, TestContext.Current.CancellationToken);

        // Act.
        var workspace = _documents.WorkspaceOf(_bodyPath);
        var layout = C4LayoutEngine.Compute(
            workspace, workspace.Views[0], C4Metrics.Default, _sidecar.Read(_bodyPath, "all"));

        // Assert.
        Assert.Equal(250, layout.Boxes["a"].X);
        Assert.Equal(400, layout.Boxes["a"].Y);
    }

    [Fact]
    public async Task DroppingOneElementOntoAnother_IsRefused_BecauseContainmentIsTheModelsToSay()
    {
        // Arrange.
        // The core contract's MoveElement carries a parent id because a tree needs one; on a C4
        // diagram that would be a claim about the architecture, not an arrangement.
        await using var session = Open();

        // Act.
        var error = await session.MoveElementAsync("a", "b", -1, TestContext.Current.CancellationToken);

        // Assert.
        Assert.Contains("not what contains it", error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(_sidecar.Read(_bodyPath, "all"));
    }

    [Fact]
    public async Task DraggingSomethingNoLongerInTheModel_IsRefused()
    {
        // Arrange.
        await using var session = Open();

        // Act.
        var error = await session.MoveElementToAsync("ghost", 10, 10, TestContext.Current.CancellationToken);

        // Assert.
        Assert.Contains("ghost", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARePartingRequest_IsRefusedForWhatItIs_NotAsABadCoordinatePair()
    {
        // Arrange.
        await using var session = Open();

        // Act.
        // Before the contract carried a position, this method did double duty: it split its
        // parent argument on a comma and took it as coordinates. A real re-parent attempt
        // therefore failed the parse and came back described as one, which is a different
        // complaint from the one the user had earned.
        var error = await session.MoveElementAsync("a", "b", -1, TestContext.Current.CancellationToken);

        // Assert.
        Assert.Contains("not what contains it", error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(_sidecar.Read(_bodyPath, "all"));
    }

    [Fact]
    public async Task ARePartingRequest_IsRefusedEvenWhenItLooksLikeCoordinates()
    {
        // Arrange.
        await using var session = Open();

        // Act.
        // The sharp end of the old encoding: an element whose id happened to read "12,34" would
        // have been taken as a position. The two gestures are separate methods now, so what the
        // argument looks like decides nothing.
        var error = await session.MoveElementAsync("a", "12,34", -1, TestContext.Current.CancellationToken);

        // Assert.
        Assert.Contains("not what contains it", error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(_sidecar.Read(_bodyPath, "all"));
    }

}
