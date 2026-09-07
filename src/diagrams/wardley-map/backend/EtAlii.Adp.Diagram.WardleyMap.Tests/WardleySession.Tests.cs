using EtAlii.Adp.Common;
using EtAlii.Adp.Diagram;
using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.History;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Diagram.WardleyMap.Tests;

/// <summary>
/// The session against the <b>real</b> history stack and the real command handler, as the C4
/// command tests do. A fake stack would assert that a command was dispatched; this asserts what
/// actually happened to the file, which is the thing that matters.
/// </summary>
public sealed class WardleySessionTests : IDisposable
{
    private readonly string _root = IoPath.Combine(IoPath.GetTempPath(), $"wardley-session-{Guid.NewGuid():N}");
    private readonly ServiceProvider _services;
    private readonly IWardleyDocumentStore _documents;
    private readonly IHistoryStack _history;
    // private readonly WardleyIdentities _identities;
    private readonly WardleyElementMapper _mapper;

    public WardleySessionTests()
    {
        Directory.CreateDirectory(_root);
        _services = new ServiceCollection().AddCommands().AddHierarchyCommandHandlers().AddWardleyMap().BuildServiceProvider();
        _documents = _services.GetRequiredService<IWardleyDocumentStore>();
        // _identities = _services.GetRequiredService<WardleyIdentities>();
        _mapper = _services.GetRequiredService<WardleyElementMapper>();
        _history = _services.GetRequiredService<IHistoryStackStore>().Get(_root);
    }

    public void Dispose()
    {
        _services.Dispose();
        TestFolder.TryDelete(_root);
    }

    private string Write(string text)
    {
        var path = IoPath.Combine(_root, "map.owm");
        File.WriteAllText(path, text);
        return path;
    }

    /// <summary>A session with the project's history, so edits are undoable.</summary>
    private WardleySession Editable(string path) => new(path, _documents, _mapper, _history);

    /// <summary>A session with no history, which is what read-only means here.</summary>
    private WardleySession ReadOnly(string path) => new(path, _documents, _mapper);

    private static IReadOnlyList<DiagramElement> AddedBy(IReadOnlyList<DiagramDelta> deltas) =>
        deltas.OfType<DiagramAddDelta>().SelectMany(add => add.Elements).ToArray();

    private static string ElementIdIn(IReadOnlyList<DiagramDelta> deltas) =>
        AddedBy(deltas).Single(element => element.Type == WardleyElementTypes.Element).Id;

    [Fact]
    public void Baseline_SendsTheWholeMapAsOneAdd()
    {
        // Arrange. Requirement 10.5 and the Performance section: one message rather than a
        // stream of per-element ones.
        var path = Write("title Tea\ncomponent Cup of Tea [0.79, 0.61]\nanchor Customer [0.95, 0.63]\n");

        // Act.
        var deltas = Editable(path).Baseline();

        // Assert. Two elements plus the evolution axis.
        Assert.Single(deltas.OfType<DiagramAddDelta>());
        Assert.Equal(3, AddedBy(deltas).Count);
    }

    [Fact]
    public void Baseline_OfAnEmptyMapIsJustTheAxis()
    {
        // Arrange. Requirement 1.4 - a map with no components opens showing its axes.
        var path = Write("title Empty\n");

        // Act.
        var elements = AddedBy(Editable(path).Baseline());

        // Assert.
        Assert.Equal(WardleyElementTypes.EvolutionAxis, Assert.Single(elements).Type);
    }

    [Fact]
    public void Baseline_IncludesAGroupForAPipeline()
    {
        // Arrange. Requirement 10.4 - the existing Group action, no new Delta invented.
        var path = Write("component Kettle [0.43, 0.35]\npipeline Kettle\n{\n  component Electric [0.63]\n}\n");

        // Act.
        var deltas = Editable(path).Baseline();

        // Assert.
        Assert.Single(deltas.OfType<DiagramGroupDelta>());
    }

    [Fact]
    public void UpdateView_WithAnUnboundedViewport_SendsNothing()
    {
        // Arrange. The baseline is already the whole map and an unbounded report admits the whole
        // map, so nothing appeared and nothing left. This used to be the module's entire answer to
        // every viewport; it is now the one case where that answer is still the right one.
        var path = Write("component Alpha [0.5, 0.5]\n");
        var session = Editable(path);
        session.Baseline();

        // Act.
        var deltas = session.UpdateView(DiagramViewport.Unbounded);

        // Assert.
        Assert.Empty(deltas);
    }

    [Fact]
    public void UpdateView_AnsweringAViewportChange_AddsWhatAppearedThenRemovesWhatLeft()
    {
        // Arrange. Two components at opposite corners of the map's own 0..1 space. The document
        // writes [visibility, maturity] and the canvas draws (maturity, 1 - visibility), so Alpha
        // sits near (0.1, 0.1) and Beta near (0.9, 0.9) - the axis inversion is why these two
        // literals do not read like the corners they are.
        var path = Write("component Alpha [0.9, 0.1]\ncomponent Beta [0.1, 0.9]\n");
        var session = Editable(path);
        session.Baseline();
        var beta = IdOfFarCorner(path);

        // Act. First look at the corner Alpha is in, then at the corner Beta is in.
        var narrowed = session.UpdateView(new DiagramViewport(0d, 0d, 0.4d, 0.4d));
        var moved = session.UpdateView(new DiagramViewport(0.6d, 0.6d, 1d, 1d));

        // Assert. Narrowing keeps Alpha and drops Beta; moving across adds Beta and drops Alpha.
        // These are the assertions that fail against a return of nothing, which is what makes this
        // the test that establishes adoption rather than one proving a call was made.
        Assert.Empty(AddedBy(narrowed));
        Assert.Equal(beta, Assert.Single(RemovedBy(narrowed)));

        Assert.Equal(beta, Assert.Single(AddedBy(moved)).Id);
        Assert.Single(RemovedBy(moved));

        // And Add comes before Remove, which is the order the reference implementations use.
        Assert.IsType<DiagramAddDelta>(moved[0]);
        Assert.IsType<DiagramRemoveDelta>(moved[1]);
    }

    [Fact]
    public void ADocumentChangeUnderANarrowedViewport_DoesNotResendTheCulledElements()
    {
        // Arrange. The interaction rather than the method: the change path and the viewport path
        // both render through one viewport-aware place, and when they do not, an ordinary edit
        // silently re-sends everything the viewport has just culled. Nothing catches that until
        // somebody edits while zoomed in, which is why this test is named for the two paths
        // meeting rather than for either of them.
        var path = Write("component Alpha [0.9, 0.1]\ncomponent Beta [0.1, 0.9]\n");
        var session = Editable(path);
        session.Baseline();
        session.UpdateView(new DiagramViewport(0d, 0d, 0.4d, 0.4d));

        IReadOnlyList<DiagramDelta> received = [];
        session.Changed += (_, args) => received = args.Deltas;

        // Act. Move Alpha, which is the component inside the viewport, and leave Beta where it is.
        File.WriteAllText(path, "component Alpha [0.85, 0.15]\ncomponent Beta [0.1, 0.9]\n");
        _documents.Reload(path);

        // Assert. The edit arrives, and Beta - culled by the viewport a moment ago - does not come
        // back with it.
        var added = AddedBy(received);
        Assert.NotEmpty(added);
        Assert.DoesNotContain(added, element => element.X > 0.5d);
    }

    [Fact]
    public void UpdateView_KeepsTheAxisAndALinkWhoseEndpointIsInView()
    {
        // Arrange. Both carry a placeholder position: a link is emitted at (0, 0) because it is
        // drawn between its endpoints, and the evolution axis at (0, 0) because it is the map's
        // frame rather than a thing on the map. A filter judging either by that point would keep
        // them only while the reader happened to be looking at the top-left corner, and cull every
        // relationship everywhere else.
        var path = Write("component Alpha [0.9, 0.1]\ncomponent Beta [0.1, 0.9]\nAlpha->Beta\n");
        var session = Editable(path);
        session.Baseline();
        var corner = new DiagramViewport(0.6d, 0.6d, 1d, 1d);

        // Act. Look only at Beta's corner - the far one from that placeholder position.
        var deltas = session.UpdateView(corner);
        var held = Visible(path, corner);

        // Assert. Nothing was culled: the axis and the link are still held, and so is Alpha, pulled
        // in as the link's other endpoint - a link delivered with one end missing draws to nothing.
        Assert.Empty(deltas.OfType<DiagramRemoveDelta>());
        Assert.Contains(held, element => element.Type == WardleyElementTypes.EvolutionAxis);
        Assert.Contains(held, element => element.Type == WardleyElementTypes.Link);
        Assert.Equal(2, held.Count(element => element.Type == WardleyElementTypes.Element));
    }

    /// <summary>What a viewport admits, asked of the mapper directly.</summary>
    private IReadOnlyList<DiagramElement> Visible(string path, DiagramViewport viewport)
    {
        var map = WardleyParser.Parse(_documents.GetOrLoad(path));
        return _mapper.Visible(map, _documents.Identities(path), viewport);
    }

    /// <summary>The id of the component in the bottom-right of the map, found by its position.</summary>
    private string IdOfFarCorner(string path) =>
        Visible(path, DiagramViewport.Unbounded)
            .Single(element => element.Type == WardleyElementTypes.Element && element.X > 0.5d)
            .Id;

    private static IReadOnlyList<string> RemovedBy(IReadOnlyList<DiagramDelta> deltas) =>
        deltas.OfType<DiagramRemoveDelta>().SelectMany(remove => remove.ElementIds).ToArray();

    [Fact]
    public async Task MoveElementAsync_IsRefused_BecauseDraggingIsNotReparenting()
    {
        // Arrange.
        var path = Write("component Alpha [0.5, 0.5]\n");

        // Act.
        var error = await Editable(path).MoveElementAsync("any", "other", 0, TestContext.Current.CancellationToken);

        // Assert. The same refusal C4Session makes, for the same reason.
        Assert.Equal("Dragging a component changes where it sits on the map, not what contains it.", error);
    }

    [Fact]
    public async Task MoveElementToAsync_RefusesOnAReadOnlyMap()
    {
        // Arrange. No history is what read-only means (Requirement 7.5).
        var path = Write("component Alpha [0.5, 0.5]\n");

        // Act.
        var error = await ReadOnly(path).MoveElementToAsync("any", 0.5d, 0.5d, TestContext.Current.CancellationToken);

        // Assert.
        Assert.Equal("This map is read-only.", error);
    }

    [Fact]
    public async Task MoveElementToAsync_WritesTheDocumentsOwnAxesBackIntoTheFile()
    {
        // Arrange. A drag arrives in canvas coordinates and must land in the file as
        // [visibility, maturity] - the inverse conversion, through the one function that owns it.
        var path = Write("component Alpha [0.90, 0.10]\n");
        var session = Editable(path);
        var elementId = ElementIdIn(session.Baseline());

        // Act. Canvas (0.8, 0.4) is maturity 0.8, visibility 0.6.
        var error = await session.MoveElementToAsync(elementId, 0.8d, 0.4d, TestContext.Current.CancellationToken);

        // Assert.
        Assert.Equal("", error);
        Assert.Equal("component Alpha [0.6, 0.8]\n", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task MoveElementToAsync_ChangesOnlyTheLineItTouches()
    {
        // Arrange. Requirement 3.2, end to end through the session.
        const string text = """
            // A map with things worth keeping.
            title Tea shop
            component Alpha [0.90, 0.10] // why it sits here
            component Beta [0.50, 0.50]
            Alpha->Beta
            """;
        var path = Write(text);
        var session = Editable(path);
        var elementId = AddedBy(session.Baseline())
            .Single(element => element.Type == WardleyElementTypes.Element
                && WardleyElementPayload.Parser.ParseFrom(element.Payload.Span).Name == "Alpha")
            .Id;

        // Act.
        await session.MoveElementToAsync(elementId, 0.2d, 0.7d, TestContext.Current.CancellationToken);

        // Assert. The comment, the title, the other component and the link are untouched.
        var after = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
        Assert.Contains("// A map with things worth keeping.", after, StringComparison.Ordinal);
        Assert.Contains("component Alpha [0.3, 0.2] // why it sits here", after, StringComparison.Ordinal);
        Assert.Contains("component Beta [0.50, 0.50]", after, StringComparison.Ordinal);
        Assert.Contains("Alpha->Beta", after, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MoveElementToAsync_ClampsAPointOutsideTheMap()
    {
        // Arrange. Requirement 7.3 - a component cannot be more evolved than commodity.
        var path = Write("component Alpha [0.5, 0.5]\n");
        var session = Editable(path);
        var elementId = ElementIdIn(session.Baseline());

        // Act.
        await session.MoveElementToAsync(elementId, 1.7d, -0.4d, TestContext.Current.CancellationToken);

        // Assert.
        Assert.Equal("component Alpha [1, 1]\n", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task MoveElementToAsync_IsUndoable_AndTheUndoRestoresTheFileExactly()
    {
        // Arrange. Requirement 7.2 - a drag is a document edit on the project's history.
        const string text = "component Alpha [0.90, 0.10] // kept\n";
        var path = Write(text);
        var session = Editable(path);
        var elementId = ElementIdIn(session.Baseline());

        // Act.
        await session.MoveElementToAsync(elementId, 0.8d, 0.4d, TestContext.Current.CancellationToken);
        var moved = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
        await _history.UndoAsync(TestContext.Current.CancellationToken);

        // Assert. Byte-identical to before the drag, comment included.
        Assert.NotEqual(text, moved);
        Assert.Equal(text, await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task MoveElementToAsync_ReportsARefusalForAnElementThatIsNotThere()
    {
        // Arrange. Requirement 9.4 - a rejection carries a message for the user rather than
        // throwing, and Requirement 9.5 checks preconditions against current state.
        var path = Write("component Alpha [0.5, 0.5]\n");

        // Act.
        var error = await Editable(path)
            .MoveElementToAsync("not-an-element", 0.5d, 0.5d, TestContext.Current.CancellationToken);

        // Assert.
        Assert.Equal("That element is no longer on this map.", error);
    }

    [Fact]
    public async Task MoveElementToAsync_MovesAPipelineChildAlongItsEvolutionAxisOnly()
    {
        // Arrange. Requirement 7.4 - a child's visibility is its parent's, and the format gives
        // it nowhere to write one of its own.
        var path = Write("component Kettle [0.43, 0.35]\npipeline Kettle\n{\n  component Electric [0.63]\n}\n");
        var session = Editable(path);
        var childId = AddedBy(session.Baseline())
            .Single(element => element.Type == WardleyElementTypes.Element
                && WardleyElementPayload.Parser.ParseFrom(element.Payload.Span).Name == "Electric")
            .Id;

        // Act. A drag that would also change visibility, which a child cannot express.
        await session.MoveElementToAsync(childId, 0.9d, 0.9d, TestContext.Current.CancellationToken);

        // Assert. One number moved; the parent's line is untouched.
        var after = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
        Assert.Contains("component Electric [0.9]", after, StringComparison.Ordinal);
        Assert.Contains("component Kettle [0.43, 0.35]", after, StringComparison.Ordinal);
    }

    [Fact]
    public void DocumentChanged_SendsTheDifferenceToTheConnection()
    {
        // Arrange. Requirement 10.7 - an edit made outside ADP arrives as deltas.
        var path = Write("component Alpha [0.9, 0.1]\n");
        var session = Editable(path);
        session.Baseline();

        var received = new List<DiagramDelta>();
        session.Changed += (_, args) => received.AddRange(args.Deltas);

        // Act.
        File.WriteAllText(path, "component Alpha [0.2, 0.8]\n");
        _documents.Reload(path);

        // Assert. One add carrying the element in its new state, never a remove and an add.
        var add = Assert.Single(received.OfType<DiagramAddDelta>());
        Assert.Single(add.Elements);
        Assert.Empty(received.OfType<DiagramRemoveDelta>());
    }

    [Fact]
    public void DocumentChanged_IgnoresAnotherMapsChange()
    {
        // Arrange.
        var path = Write("component Alpha [0.9, 0.1]\n");
        var other = IoPath.Combine(_root, "other.owm");
        File.WriteAllText(other, "component Beta [0.5, 0.5]\n");
        var session = Editable(path);
        session.Baseline();

        var raised = 0;
        session.Changed += (_, _) => raised++;

        // Act.
        _documents.GetOrLoad(other);
        _documents.Reload(other);

        // Assert.
        Assert.Equal(0, raised);
    }

    [Fact]
    public async Task DisposeAsync_UnsubscribesFromTheStore()
    {
        // Arrange.
        var path = Write("component Alpha [0.9, 0.1]\n");
        var session = Editable(path);
        session.Baseline();
        var raised = 0;
        session.Changed += (_, _) => raised++;

        // Act.
        await session.DisposeAsync();
        await File.WriteAllTextAsync(path, "component Alpha [0.2, 0.8]\n", TestContext.Current.CancellationToken);
        _documents.Reload(path);

        // Assert. A disposed session must not keep receiving, or a closed tab holds the
        // document alive and answers for a connection that has gone.
        Assert.Equal(0, raised);
    }
}
