using EtAlii.Adp.Documents;
using EtAlii.Adp.Files;
using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.History;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.CausalLoopDiagram.Tests;

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
    private readonly CausalLoopDocumentStore _store = new(new FileWriter());

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

        _provider = new ServiceCollection().AddCommands().AddHierarchyCommandHandlers().AddCausalLoop().BuildServiceProvider();
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
    public async Task Baseline_DrawsTheWholeDocument()
    {
        // Arrange & act.
        var session = Session();
        var add = Assert.IsType<DiagramAddDelta>(Assert.Single(await session.BaselineAsync(TestContext.Current.CancellationToken)));

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
    public async Task AViewChange_SendsWhatCameIntoView_AndTakesBackWhatLeft()
    {
        // Arrange. The corpus triangle no longer works here: a link travels wherever its span
        // touches the view and brings its ends along, so in a triangle every narrowing keeps
        // everything. What leaves a view is what is neither in it nor anchoring a line that
        // crosses it - an UNLINKED variable far away, pinned by authored positions so the
        // geometry is this test's own rather than the layout's.
        var bodyPath = IoPath.Combine(_root, "coasts.cld");
        var registrationPath = IoPath.Combine(_root, "coasts.adp");
        await File.WriteAllTextAsync(bodyPath, "causal-loop 1\r\nvariable near \"Near\"\r\nvariable far \"Far\"\r\n", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(
            registrationPath,
            "systems/causal-loop-diagram\r\nbody: coasts.cld\r\nlayout:\r\n  variable:near: 0 0\r\n  variable:far: 10000 0\r\n", TestContext.Current.CancellationToken);
        var session = new CausalLoopSession(bodyPath, registrationPath, _store, new CausalLoopElementMapper());
        var all = await session.BaselineElementsAsync(TestContext.Current.CancellationToken);
        var near = all.Single(element => element.Id == "variable:near");
        var far = all.Single(element => element.Id == "variable:far");

        // Act.
        var narrowed = session.UpdateView(Around(near));
        var moved = session.UpdateView(Around(far));

        // Assert: narrowing took back the far variable and added nothing.
        Assert.Contains(far.Id, narrowed.OfType<DiagramRemoveDelta>().SelectMany(delta => delta.ElementIds));
        Assert.DoesNotContain(
            narrowed.OfType<DiagramAddDelta>().SelectMany(delta => delta.Elements),
            element => element.Id == far.Id);

        // ...and moving across took back the near one and added the far one, in that order: the
        // shared diff removes first, one order for every module (backend-centralization R4.5).
        Assert.Contains(
            moved.OfType<DiagramAddDelta>().SelectMany(delta => delta.Elements),
            element => element.Id == far.Id);
        Assert.Contains(near.Id, moved.OfType<DiagramRemoveDelta>().SelectMany(delta => delta.ElementIds));
        Assert.IsType<DiagramRemoveDelta>(moved[0]);
        Assert.IsType<DiagramAddDelta>(moved[1]);
    }

    [Fact]
    public async Task AViewportAdmittingEverything_ChangesNothing()
    {
        // Arrange & act.
        var session = Session();
        await session.BaselineAsync(TestContext.Current.CancellationToken);

        // Assert.
        // Everything the baseline drew is still visible, so nothing appeared and nothing left.
        Assert.Empty(session.UpdateView(new DiagramViewport(-100_000, -100_000, 100_000, 100_000)));
    }

    /// <summary>
    /// Requirement 10.3. The layout must not depend on where the reader is looking, or the
    /// diagram rearranges itself under them as they pan.
    /// </summary>
    [Fact]
    public async Task TheLayoutDoesNotDependOnTheViewport()
    {
        // Arrange.
        var session = Session();
        var before = (await session.BaselineElementsAsync(TestContext.Current.CancellationToken))
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
    public async Task AStoredPosition_OverridesTheComputedOne()
    {
        // Arrange.
        await File.WriteAllTextAsync(
            _registrationPath,
            "systems/causal-loop-diagram\r\nbody: feedback.cld\r\nlayout:\r\n  variable:a: 5000 6000\r\n", TestContext.Current.CancellationToken);

        // Act.
        var elements = Assert.IsType<DiagramAddDelta>(Assert.Single(await Session().BaselineAsync(TestContext.Current.CancellationToken))).Elements;
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
        await session.BaselineAsync(TestContext.Current.CancellationToken);

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
    public async Task ADocumentChange_IsFilteredThroughTheViewportTheConnectionReported()
    {
        // Arrange.
        var session = Session();
        var all = await session.BaselineElementsAsync(TestContext.Current.CancellationToken);
        var one = all.First(element => element.Type == CausalLoopElementMapper.VariableType);
        session.UpdateView(Around(one));

        IReadOnlyList<DiagramDelta> pushed = [];
        session.Changed += (_, args) => pushed = args.Deltas;

        // Act: the document changes underneath while the reader is zoomed in.
        await File.WriteAllTextAsync(_bodyPath, Corpus + "variable d \"Delta\"\r\n", TestContext.Current.CancellationToken);
        await _store.ReloadAsync(_bodyPath, TestContext.Current.CancellationToken);

        // Assert: what was pushed is what the viewport admits, not the whole document.
        var added = pushed.OfType<DiagramAddDelta>().SelectMany(delta => delta.Elements).ToArray();
        Assert.True(added.Length < all.Count, $"pushed {added.Length} of {all.Count} - the change was not filtered");
    }

    [Fact]
    public async Task AChangeToAnotherDocument_IsNotThisSessionsBusiness()
    {
        // Arrange: the store serves every open diagram, so a session has to filter by its own. The
        // diff sends only what differs, so a session that re-rendered on another document's
        // change would stay silent whenever its own view had not moved. So this session's view is
        // changed WITHOUT telling it - a position written straight into its registration - and a
        // session that re-rendered on the other document's change would now push the variable.
        var session = Session();
        await session.BaselineAsync(TestContext.Current.CancellationToken);
        var otherPath = IoPath.Combine(_root, "other.cld");
        await File.WriteAllTextAsync(otherPath, Corpus, TestContext.Current.CancellationToken);
        _ = await _store.GetOrLoadAsync(otherPath, TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(
            _registrationPath,
            "systems/causal-loop-diagram\r\nbody: feedback.cld\r\nlayout:\r\n  variable:a: 5000 6000\r\n", TestContext.Current.CancellationToken);

        var pushed = new List<IReadOnlyList<DiagramDelta>>();
        session.Changed += (_, args) => pushed.Add(args.Deltas);

        // Act.
        await _store.ReloadAsync(otherPath, TestContext.Current.CancellationToken);
        var pushedForTheOther = pushed.Count;
        await _store.ReloadAsync(_bodyPath, TestContext.Current.CancellationToken);

        // Assert: nothing for the other document, and the same change to its own document does
        // move the variable - which is what makes the silence mean something.
        Assert.Equal(0, pushedForTheOther);
        var moved = Assert.Single(
            Assert.Single(pushed).OfType<DiagramAddDelta>().SelectMany(delta => delta.Elements),
            element => element.Id == "variable:a");
        Assert.Equal(5000, moved.X);
    }

    /// <summary>
    /// backend-centralization R5.2: only a read failure is caught. This session used to catch
    /// every failure while re-rendering and log it as its own error, which hid a defect behind
    /// the same kind of line as a locked file; a defect now reaches whoever raised the change.
    /// </summary>
    [Fact]
    public async Task ADefectWhileReRenderingAChange_ReachesTheCaller()
    {
        // Arrange.
        var store = new FailingStore(_store);
        var session = new CausalLoopSession(_bodyPath, _registrationPath, store, new CausalLoopElementMapper());
        await session.BaselineAsync(TestContext.Current.CancellationToken);
        var pushed = 0;
        session.Changed += (_, _) => pushed++;
        store.Failure = new InvalidOperationException("a defect");

        // Act & assert.
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await _store.ReloadAsync(_bodyPath, TestContext.Current.CancellationToken));
        Assert.Equal(0, pushed);
    }

    /// <summary>
    /// backend-centralization R5.1: a file that vanished or locked mid-reload costs that push and
    /// nothing else - no exception reaches the caller, and the next change tries again.
    /// </summary>
    [Fact]
    public async Task AReadFailureWhileReRenderingAChange_CostsOnlyThatPush()
    {
        // Arrange.
        var store = new FailingStore(_store);
        var session = new CausalLoopSession(_bodyPath, _registrationPath, store, new CausalLoopElementMapper());
        await session.BaselineAsync(TestContext.Current.CancellationToken);
        var pushed = new List<IReadOnlyList<DiagramDelta>>();
        session.Changed += (_, args) => pushed.Add(args.Deltas);
        await File.WriteAllTextAsync(_bodyPath, Corpus + "variable d \"Delta\"\r\n", TestContext.Current.CancellationToken);
        store.Failure = new IOException("locked");

        // Act.
        await _store.ReloadAsync(_bodyPath, TestContext.Current.CancellationToken);
        var pushedWhileLocked = pushed.Count;
        store.Failure = null;
        await _store.ReloadAsync(_bodyPath, TestContext.Current.CancellationToken);

        // Assert: nothing while locked, and the next change catches the diagram up.
        Assert.Equal(0, pushedWhileLocked);
        Assert.Contains(
            Assert.Single(pushed).OfType<DiagramAddDelta>().SelectMany(delta => delta.Elements),
            element => element.Id == "variable:d");
    }

    [Fact]
    public async Task AMissingDocument_DrawsNothingRatherThanThrowing()
    {
        // Arrange.
        var missing = IoPath.Combine(_root, "absent.cld");
        var session = new CausalLoopSession(missing, null, _store, new CausalLoopElementMapper());

        // Act & assert.
        Assert.Empty(await session.BaselineAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>The real store, except that reading from it throws whatever a test sets.</summary>
    private sealed class FailingStore(CausalLoopDocumentStore inner) : ICausalLoopDocumentStore
    {
        public Exception? Failure { get; set; }

        public event EventHandler<DocumentChangedEventArgs>? Changed
        {
            add => inner.Changed += value;
            remove => inner.Changed -= value;
        }

        public ValueTask<DocumentEntry<CausalLoopBody, CausalLoopModel>> GetOrLoadAsync(string path, CancellationToken cancellationToken) => Failure is null ? inner.GetOrLoadAsync(path, cancellationToken) : throw Failure;

        public DocumentEntry<CausalLoopBody, CausalLoopModel>? Get(string path) => Failure is null ? inner.Get(path) : throw Failure;

        public Task<DocumentSaveResult> SaveAsync(string path, CausalLoopBody document, CancellationToken cancellationToken) => inner.SaveAsync(path, document, cancellationToken);

        public Task ReloadAsync(string path, CancellationToken cancellationToken) => inner.ReloadAsync(path, cancellationToken);

        public Task BodyDeletedAsync(string path, CancellationToken cancellationToken) => inner.BodyDeletedAsync(path, cancellationToken);

        public void Forget(string path) => inner.Forget(path);
    }
}
