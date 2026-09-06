using EtAlii.Adp.Backend;
using EtAlii.Adp.Backend.Diagrams;
using EtAlii.Adp.Common;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.Sparql.Tests;

/// <summary>
/// The session: a registered <c>.rq</c> opens with computed layout and stored positions
/// overlaid, the one mutating gesture is a reposition that leaves the query byte-identical, and
/// every refusal answers with its own sentence (Requirements 2, 5, 6).
/// </summary>
public class SparqlSessionTests : IDisposable
{
    private readonly string _root;
    private readonly ServiceProvider _provider;

    public SparqlSessionTests()
    {
        _root = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _provider = new ServiceCollection()
            .AddSingleton<IReadOnlyList<DiagramDefinition>>(Diagram.Definitions)
            .AddCommands()
            .AddSparql()
            .BuildServiceProvider();
    }

    public void Dispose()
    {
        _provider.Dispose();
        TestFolder.TryDelete(_root);
    }

    private string CopyFixture(string name)
    {
        var destination = IoPath.Combine(_root, name);
        File.Copy(IoPath.Combine(AppContext.BaseDirectory, "Fixtures", name), destination);
        return destination;
    }

    private string WriteRegistration(string bodyName)
    {
        var path = IoPath.Combine(_root, "query.adp");
        File.WriteAllText(path, $"w3c/sparql\r\nbody: {bodyName}\r\n");
        return path;
    }

    private IDiagramSession Open(string bodyPath, string? registrationPath)
    {
        var factory = _provider.GetServices<IDiagramSessionFactory>()
            .Single(candidate => candidate.Origin == ServiceCollectionAddSparqlExtension.SparqlOrigin);
        return factory.Open(ShortGuid.NewShortGuid(), _root, bodyPath, registrationPath);
    }

    private static IReadOnlyList<DiagramElement> ElementsOf(IDiagramSession session)
    {
        var baseline = Assert.Single(session.Baseline());
        return Assert.IsType<DiagramAddDelta>(baseline).Elements.ToList();
    }

    /// <summary>
    /// The loop this task closes (view-delta-adoption Requirements 1.2, 1.3): a changed
    /// viewport answers with what came into view and what left it, in that order. Viewports are
    /// derived from where the elements actually are, so a layout change moves the test with the
    /// code rather than breaking it.
    /// </summary>
    [Fact]
    public async Task AChangedViewport_AddsWhatAppearedThenRemovesWhatLeft()
    {
        // Arrange.
        var body = CopyFixture("groups.rq");
        await using var session = Open(body, WriteRegistration("groups.rq"));
        var all = ElementsOf(session);
        var leftmost = all.MinBy(element => element.X)!;
        var rightmost = all.MaxBy(element => element.X)!;
        Assert.NotEqual(leftmost.Id, rightmost.Id);

        // Act.
        var narrowed = session.UpdateView(Around(leftmost));
        var moved = session.UpdateView(Around(rightmost));

        // Assert: narrowing removed the far element...
        Assert.Contains(rightmost.Id, narrowed.OfType<DiagramRemoveDelta>().SelectMany(delta => delta.ElementIds));

        // ...and moving across added it back, Add before Remove.
        Assert.Contains(moved.OfType<DiagramAddDelta>().SelectMany(delta => delta.Elements), element => element.Id == rightmost.Id);
        Assert.IsType<DiagramAddDelta>(moved[0]);
        Assert.True(moved.Count == 1 || moved[1] is DiagramRemoveDelta);
    }

    /// <summary>
    /// The interaction the two paths used to disagree about: when the change path rendered the
    /// whole model while UpdateView filtered, an ordinary edit re-sent every element the
    /// viewport had just culled. Both now render through one viewport-aware place.
    /// </summary>
    [Fact]
    public async Task ADocumentChangeUnderANarrowedViewport_DoesNotResendTheCulledElements()
    {
        // Arrange: open, then narrow the viewport to one element so the rest is culled.
        var body = CopyFixture("groups.rq");
        await using var session = Open(body, WriteRegistration("groups.rq"));
        var all = ElementsOf(session);
        var kept = all.MinBy(element => element.X)!;
        var culled = all.MaxBy(element => element.X)!;
        session.UpdateView(Around(kept));

        IReadOnlyList<DiagramDelta> received = [];
        session.Changed += (_, args) => received = args.Deltas;

        // Act: a real edit through the reloader - the path a save from the text editor takes.
        // Re-calling UpdateView would not do: with a broken UpdateView that answers [], such a
        // test passes vacuously, which is the trap this guard exists to avoid.
        // An ADDITIVE edit, deliberately: renaming a variable would change the element ids and
        // the assertion below would pass vacuously because the culled id no longer exists at
        // all. Adding a triple leaves every existing id in place, so a viewport-blind change
        // path really does re-send the culled element and really is caught.
        await File.WriteAllTextAsync(body, (await File.ReadAllTextAsync(body, TestContext.Current.CancellationToken)).Replace(
            "  ?s ex:p ?x .",
            "  ?s ex:p ?x .\n  ?s ex:fresh ?fresh .",
            StringComparison.Ordinal), TestContext.Current.CancellationToken);
        _provider.GetServices<IDiagramDocumentReloader>()
            .Single(candidate => candidate.Origin == ServiceCollectionAddSparqlExtension.SparqlOrigin)
            .Reload(_root, body);

        // Assert: the culled elements do not come back with the edit - the change path renders
        // through the same viewport the view path does.
        //
        // Deliberately NOT asserting that something arrived: the added triple lands outside the
        // narrowed viewport, so a correct implementation answers this connection with nothing at
        // all, and requiring a delta here would be asserting the bug. The discrimination is real
        // rather than vacuous - with the change path rendering unfiltered, the diff against the
        // narrowed set re-adds every culled element and this assertion fires.
        Assert.DoesNotContain(
            received.OfType<DiagramAddDelta>().SelectMany(delta => delta.Elements),
            element => element.Id == culled.Id);
    }

    /// <summary>A viewport tight around one element, in the module's own units.</summary>
    private static DiagramViewport Around(DiagramElement element) =>
        new(element.X - 1, element.Y - 1, element.X + 1, element.Y + 1);

    [Fact]
    public async Task ARegisteredQuery_OpensWithNodesEdgesRegionsAndTheHeader()
    {
        // Arrange.
        var body = CopyFixture("groups.rq");

        // Act.
        await using var session = Open(body, WriteRegistration("groups.rq"));
        var elements = ElementsOf(session);

        // Assert.
        Assert.Contains(elements, element => element.Id == "var:x" && element.Type == SparqlElementMapper.VariableType);
        Assert.Contains(elements, element => element.Type == SparqlElementMapper.EdgeType);
        Assert.Contains(elements, element => element.Id == "region:where/optional.0" && element.Type == SparqlElementMapper.RegionType);
        Assert.Contains(elements, element => element.Type == SparqlElementMapper.AnnotationType);
        Assert.Contains(elements, element => element.Id == SparqlElementMapper.HeaderId);
        Assert.DoesNotContain(elements, element => element.Id == SparqlElementMapper.TruncationId);
    }

    [Fact]
    public async Task AStoredPosition_OverlaysTheComputedOne()
    {
        // Arrange.
        var body = CopyFixture("groups.rq");
        var registration = WriteRegistration("groups.rq");
        await File.AppendAllTextAsync(registration, "layout:\r\n  var:x: 640 480\r\n", TestContext.Current.CancellationToken);

        // Act.
        await using var session = Open(body, registration);
        var elements = ElementsOf(session);

        // Assert.
        var node = Assert.Single(elements, element => element.Id == "var:x");
        Assert.Equal(640, node.X);
        Assert.Equal(480, node.Y);
    }

    [Fact]
    public async Task Repositioning_WritesTheRegistrationAndLeavesTheQueryByteIdentical()
    {
        // Arrange.
        var body = CopyFixture("groups.rq");
        var original = await File.ReadAllBytesAsync(body, TestContext.Current.CancellationToken);
        await using var session = Open(body, WriteRegistration("groups.rq"));

        // Act.
        var refusal = await session.MoveElementToAsync("var:x", 320, 200, CancellationToken.None);

        // Assert.
        Assert.Equal("", refusal);
        Assert.Equal(original, await File.ReadAllBytesAsync(body, TestContext.Current.CancellationToken));
        Assert.Contains("var:x: 320 200", await File.ReadAllTextAsync(IoPath.Combine(_root, "query.adp"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Repositioning_IsOneUndoAway_AndUndoAlsoLeavesTheQueryAlone()
    {
        // Arrange.
        var body = CopyFixture("groups.rq");
        var original = await File.ReadAllBytesAsync(body, TestContext.Current.CancellationToken);
        var registration = WriteRegistration("groups.rq");
        var before = await File.ReadAllTextAsync(registration, TestContext.Current.CancellationToken);
        await using var session = Open(body, registration);
        await session.MoveElementToAsync("var:x", 320, 200, CancellationToken.None);

        // Act.
        var history = _provider.GetRequiredService<IHistoryStackStore>().Get(_root);
        await history.UndoAsync(CancellationToken.None);

        // Assert.
        Assert.Equal(before, await File.ReadAllTextAsync(registration, TestContext.Current.CancellationToken));
        Assert.Equal(original, await File.ReadAllBytesAsync(body, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("anon:0", "anonymous variable")]
    [InlineData("edge:var:s|ex:p|var:x|0", "endpoints")]
    [InlineData("note:where/filter.0", "annotation stays")]
    [InlineData(SparqlElementMapper.HeaderId, "header band")]
    [InlineData(SparqlElementMapper.TruncationId, "banner")]
    public async Task EachUnmovableSurface_RefusesWithItsOwnReason(string elementId, string expected)
    {
        // Arrange.
        var body = CopyFixture("groups.rq");
        await using var session = Open(body, WriteRegistration("groups.rq"));

        // Act.
        var refusal = await session.MoveElementToAsync(elementId, 10, 10, CancellationToken.None);

        // Assert.
        Assert.Contains(expected, refusal, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ABareUnregisteredQuery_RefusesNamingTheMissingRegistration()
    {
        // Arrange.
        var body = CopyFixture("groups.rq");
        await using var session = Open(body, registrationPath: null);

        // Act.
        var refusal = await session.MoveElementToAsync("var:x", 10, 10, CancellationToken.None);

        // Assert.
        Assert.Contains("without a registration", refusal);
        Assert.Contains("Register the file", refusal);
    }

    [Fact]
    public async Task Reparenting_IsRefusedBecauseStructureComesFromTheText()
    {
        // Arrange.
        var body = CopyFixture("groups.rq");
        await using var session = Open(body, WriteRegistration("groups.rq"));

        // Act.
        var refusal = await session.MoveElementAsync("var:x", "region:where/optional.0", 0, CancellationToken.None);

        // Assert.
        Assert.Contains("comes from its text", refusal);
    }

    [Fact]
    public async Task AnExternalEdit_ReachesTheOpenSessionAsDeltas()
    {
        // Arrange.
        var body = CopyFixture("lf-line-endings.rq");
        await using var session = Open(body, WriteRegistration("lf-line-endings.rq"));
        ElementsOf(session);

        IReadOnlyList<DiagramDelta> received = [];
        session.Changed += (_, args) => received = args.Deltas;

        // Act: the text editor is where these files are edited, so this is the normal path.
        await File.WriteAllTextAsync(body, "PREFIX ex: <http://example.org/>\nSELECT ?s ?extra\nWHERE { ?s ex:p ?o . ?s ex:q ?extra }\n", TestContext.Current.CancellationToken);
        _provider.GetServices<IDiagramDocumentReloader>()
            .Single(candidate => candidate.Origin == ServiceCollectionAddSparqlExtension.SparqlOrigin)
            .Reload(_root, body);

        // Assert.
        var add = Assert.IsType<DiagramAddDelta>(received[0]);
        Assert.Contains(add.Elements, element => element.Id == "var:extra");
    }

    [Fact]
    public async Task AQueryThatDoesNotParse_OpensWithOnlyItsHeader()
    {
        // Arrange: a broken file still opens - the unavailable state is core's, and the session
        // has nothing to draw but the frame (Requirement 1.3).
        var body = CopyFixture("broken.rq");

        // Act.
        await using var session = Open(body, WriteRegistration("broken.rq"));
        var elements = ElementsOf(session);

        // Assert.
        Assert.Equal(SparqlElementMapper.HeaderId, Assert.Single(elements).Id);
    }
}
