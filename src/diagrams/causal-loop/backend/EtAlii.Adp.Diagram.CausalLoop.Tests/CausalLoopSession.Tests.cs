using EtAlii.Adp.Backend;
using EtAlii.Adp.Backend.Diagrams;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.CausalLoop.Tests;

/// <summary>
/// The session over the document store (causal-loop-diagram Requirements 10.2, 10.3, 10.4): the
/// whole document laid out and then filtered, a view change answered with what appeared and what
/// left, and an arrangement stored in the registration rather than in the body.
/// </summary>
public class CausalLoopSessionTests : IDisposable
{
    private readonly string _root;
    private readonly string _bodyPath;
    private readonly string _registrationPath;
    private readonly ServiceProvider _provider;
    private readonly CausalLoopDocumentStore _store = new();

    private const string Corpus =
        "causal-loop 1\r\n"
        + "variable a \"Alpha\"\r\n"
        + "variable b \"Beta\"\r\n"
        + "variable c \"Gamma\"\r\n"
        + "link a -> b +\r\n"
        + "link b -> c +\r\n"
        + "link c -> a +\r\n"
        + "loop R1 \"the ring\" a b c\r\n";

    public CausalLoopSessionTests()
    {
        _root = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _bodyPath = IoPath.Combine(_root, "feedback.cld");
        _registrationPath = IoPath.Combine(_root, "feedback.adp");
        File.WriteAllText(_bodyPath, Corpus);
        File.WriteAllText(_registrationPath, "systems/causal-loop-diagram\r\nbody: feedback.cld\r\n");

        _provider = new ServiceCollection().AddCommands().AddCausalLoop().BuildServiceProvider();
    }

    public void Dispose()
    {
        _provider.Dispose();
        TestFolder.TryDelete(_root);
        GC.SuppressFinalize(this);
    }

    private CausalLoopSession Session(bool withHistory = true, string? registration = null) => new(
        _bodyPath,
        registration ?? _registrationPath,
        _store,
        new CausalLoopElementMapper(),
        withHistory ? _provider.GetRequiredService<IHistoryStackStore>().Get(_root) : null);

    /// <summary>A viewport tight around one element, in the module's own units.</summary>
    private static DiagramViewport Around(DiagramElement element) =>
        new(element.X - 1, element.Y - 1, element.X + 1, element.Y + 1);

    [Fact]
    public void Baseline_DrawsTheWholeDocument()
    {
        // Arrange & act.
        var session = Session();
        var add = Assert.IsType<DiagramAddDelta>(Assert.Single(session.Baseline()));

        // Assert.
        Assert.NotEmpty(add.Elements);
        Assert.Equal(3, add.Elements.Count(element => element.Type == CausalLoopElementMapper.VariableType));
        Assert.Equal(3, add.Elements.Count(element => element.Type == CausalLoopElementMapper.LinkType));
        Assert.Single(add.Elements, element => element.Type == CausalLoopElementMapper.LoopType);
    }

    /// <summary>
    /// The behavioural test the specification asks for (Requirement 10.4), written to fail
    /// against a <c>return []</c>. A client-side assertion that the viewport was reported would
    /// pass against a session that answers with nothing, which is why this one lives here.
    /// </summary>
    [Fact]
    public void AViewChange_SendsWhatCameIntoView_AndTakesBackWhatLeft()
    {
        // Arrange.
        var session = Session();
        var all = Assert.IsType<DiagramAddDelta>(Assert.Single(session.Baseline())).Elements;
        var variables = all.Where(element => element.Type == CausalLoopElementMapper.VariableType).ToArray();
        var leftmost = variables.MinBy(element => element.X)!;
        var rightmost = variables.MaxBy(element => element.X)!;
        Assert.NotEqual(leftmost.Id, rightmost.Id);

        // Act.
        var narrowed = session.UpdateView(Around(leftmost));
        var moved = session.UpdateView(Around(rightmost));

        // Assert: narrowing took back the far variable and added nothing.
        Assert.Contains(rightmost.Id, narrowed.OfType<DiagramRemoveDelta>().SelectMany(delta => delta.ElementIds));
        Assert.DoesNotContain(
            narrowed.OfType<DiagramAddDelta>().SelectMany(delta => delta.Elements),
            element => element.Id == rightmost.Id);

        // ...and moving across added the far one and took back the near one, in that order.
        Assert.Contains(
            moved.OfType<DiagramAddDelta>().SelectMany(delta => delta.Elements),
            element => element.Id == rightmost.Id);
        Assert.Contains(leftmost.Id, moved.OfType<DiagramRemoveDelta>().SelectMany(delta => delta.ElementIds));
        Assert.IsType<DiagramAddDelta>(moved[0]);
        Assert.IsType<DiagramRemoveDelta>(moved[1]);
    }

    [Fact]
    public void AViewportAdmittingEverything_ChangesNothing()
    {
        // Arrange & act.
        var session = Session();
        session.Baseline();

        // Assert.
        // Everything the baseline drew is still visible, so nothing appeared and nothing left.
        Assert.Empty(session.UpdateView(new DiagramViewport(-100_000, -100_000, 100_000, 100_000)));
    }

    /// <summary>
    /// Requirement 10.3. The layout must not depend on where the reader is looking, or the
    /// diagram rearranges itself under them as they pan.
    /// </summary>
    [Fact]
    public void TheLayoutDoesNotDependOnTheViewport()
    {
        // Arrange.
        var session = Session();
        var before = Assert.IsType<DiagramAddDelta>(Assert.Single(session.Baseline())).Elements
            .ToDictionary(element => element.Id, element => (element.X, element.Y), StringComparer.Ordinal);

        var anchor = before.First(entry => entry.Key.StartsWith("variable:", StringComparison.Ordinal));

        // Act: narrow to one variable, then open right back up.
        session.UpdateView(new DiagramViewport(anchor.Value.X - 1, anchor.Value.Y - 1, anchor.Value.X + 1, anchor.Value.Y + 1));
        var reopened = session.UpdateView(DiagramViewport.Unbounded);

        // Assert: what came back is at exactly the coordinates it had before.
        foreach (var element in reopened.OfType<DiagramAddDelta>().SelectMany(delta => delta.Elements))
        {
            Assert.Equal(before[element.Id], (element.X, element.Y));
        }
    }

    [Fact]
    public void AStoredPosition_OverridesTheComputedOne()
    {
        // Arrange.
        File.WriteAllText(
            _registrationPath,
            "systems/causal-loop-diagram\r\nbody: feedback.cld\r\nlayout:\r\n  variable:a: 5000 6000\r\n");

        // Act.
        var elements = Assert.IsType<DiagramAddDelta>(Assert.Single(Session().Baseline())).Elements;
        var moved = Assert.Single(elements, element => element.Id == "variable:a");

        // Assert.
        Assert.Equal(5000, moved.X);
        Assert.Equal(6000, moved.Y);
    }

    [Fact]
    public async Task ARepositionStoresInTheRegistration_AndNeverTouchesTheBody()
    {
        // Arrange.
        var before = await File.ReadAllTextAsync(_bodyPath, TestContext.Current.CancellationToken);
        await using var session = Session();
        session.Baseline();

        // Act.
        var refusal = await session.MoveElementToAsync("variable:a", 120, 240, TestContext.Current.CancellationToken);

        // Assert.
        Assert.Equal("", refusal);
        Assert.Contains("layout:", await File.ReadAllTextAsync(_registrationPath, TestContext.Current.CancellationToken), StringComparison.Ordinal);
        Assert.Equal(before, await File.ReadAllTextAsync(_bodyPath, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task MovingALinkOrALoop_IsRefused_BecauseNeitherHasAPosition()
    {
        // Arrange.
        await using var session = Session();

        // Act & assert.
        var link = await session.MoveElementToAsync("link:a|b", 10, 10, TestContext.Current.CancellationToken);
        var loop = await session.MoveElementToAsync("loop:R1", 10, 10, TestContext.Current.CancellationToken);

        Assert.Contains("Only a variable can be moved", link, StringComparison.Ordinal);
        Assert.Equal(link, loop);
    }

    [Fact]
    public async Task WithoutARegistration_ArrangingIsRefusedWithSomewhereToPutIt()
    {
        // Arrange.
        await using var session = Session(registration: "");

        // Act & assert.
        var refusal = await session.MoveElementToAsync("variable:a", 1, 1, TestContext.Current.CancellationToken);
        Assert.Contains("nowhere to store a position", refusal, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WithoutHistory_ArrangingSaysReadOnly()
    {
        // Arrange.
        await using var session = Session(withHistory: false);

        // Act & assert.
        Assert.Equal(
            "This diagram is read-only.",
            await session.MoveElementToAsync("variable:a", 1, 1, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Reparenting_IsRefusedWithASentence()
    {
        // Arrange.
        await using var session = Session();

        // Act & assert.
        var answer = await session.MoveElementAsync("variable:a", "variable:b", 0, TestContext.Current.CancellationToken);
        Assert.Contains("no parent to move it under", answer, StringComparison.Ordinal);
    }

    /// <summary>
    /// The trap the board found on timeline: a change handler that renders unfiltered re-sends
    /// everything the viewport just culled, and it is invisible until somebody edits a document
    /// while zoomed in.
    /// </summary>
    [Fact]
    public void ADocumentChange_IsFilteredThroughTheViewportTheConnectionReported()
    {
        // Arrange.
        var session = Session();
        var all = Assert.IsType<DiagramAddDelta>(Assert.Single(session.Baseline())).Elements;
        var one = all.First(element => element.Type == CausalLoopElementMapper.VariableType);
        session.UpdateView(Around(one));

        IReadOnlyList<DiagramDelta> pushed = [];
        session.Changed += (_, args) => pushed = args.Deltas;

        // Act: the document changes underneath while the reader is zoomed in.
        File.WriteAllText(_bodyPath, Corpus + "variable d \"Delta\"\r\n");
        _store.Reload(_bodyPath);

        // Assert: what was pushed is what the viewport admits, not the whole document.
        var added = pushed.OfType<DiagramAddDelta>().SelectMany(delta => delta.Elements).ToArray();
        Assert.True(added.Length < all.Count, $"pushed {added.Length} of {all.Count} - the change was not filtered");
    }

    [Fact]
    public void AnUnreadableDocument_DrawsNothingRatherThanThrowing()
    {
        // Arrange.
        var missing = IoPath.Combine(_root, "absent.cld");
        var session = new CausalLoopSession(missing, null, _store, new CausalLoopElementMapper());

        // Act & assert.
        Assert.Empty(session.Baseline());
    }
}
