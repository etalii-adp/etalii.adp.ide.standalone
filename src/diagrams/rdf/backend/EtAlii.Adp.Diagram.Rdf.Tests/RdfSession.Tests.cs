using System.Globalization;
using System.Text;
using EtAlii.Adp.Common;
using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.History;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.Rdf.Tests;

/// <summary>
/// The session: a registered .ttl opens with computed layout and stored positions overlaid, a
/// reposition is one undo away while the RDF file never changes by a byte, and the refusal set -
/// blank nodes, edges, unregistered bare files - answers with its sentences (rdf-diagram
/// Requirement 4).
/// </summary>
public class RdfSessionTests : IDisposable
{
    private readonly string _root;
    private readonly ServiceProvider _provider;

    public RdfSessionTests()
    {
        _root = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _provider = new ServiceCollection()
            .AddSingleton<IReadOnlyList<DiagramDefinition>>(Diagram.Definitions)
            .AddCommands().AddHierarchyCommandHandlers()
            .AddRdf()
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

    private string WriteRegistration(string bodyName, string extra = "")
    {
        var path = IoPath.Combine(_root, "graph.adp");
        File.WriteAllText(path, $"w3c/rdf\r\nbody: {bodyName}\r\n{extra}");
        return path;
    }

    private IDiagramSession Open(string bodyPath, string? registrationPath)
    {
        var factory = _provider.GetServices<IDiagramSessionFactory>()
            .Single(candidate => candidate.Origin == ServiceCollectionAddRdfExtension.RdfOrigin);
        return factory.Open(ShortGuid.NewShortGuid(), _root, bodyPath, registrationPath);
    }

    private static IReadOnlyList<DiagramElement> ElementsOf(IDiagramSession session)
    {
        var baseline = Assert.Single(session.Baseline());
        return Assert.IsType<DiagramAddDelta>(baseline).Elements.ToList();
    }

    [Fact]
    public async Task ARegisteredTurtleFile_OpensWithCardsAndEdges()
    {
        // Arrange.
        var body = CopyFixture("constructs.ttl");

        // Act.
        await using var session = Open(body, WriteRegistration("constructs.ttl"));
        var elements = ElementsOf(session);

        // Assert.
        Assert.Contains(elements, element => element.Id == "res:http://example.org/alice" && element.Type == RdfElementMapper.ResourceType);
        Assert.Contains(elements, element => element.Type == RdfElementMapper.EdgeType);
        // Nothing truncated in a small file, so no banner.
        Assert.DoesNotContain(elements, element => element.Type == RdfElementMapper.TruncationType);
    }

    [Fact]
    public async Task AStoredPosition_WinsOverTheComputedOne()
    {
        // Arrange.
        var body = CopyFixture("crlf-line-endings.ttl");
        var adp = WriteRegistration("crlf-line-endings.ttl", "layout:\r\n  res:http://example.org/a: 555 666\r\n");

        // Act.
        await using var session = Open(body, adp);
        var elements = ElementsOf(session);

        // Assert.
        var moved = elements.Single(element => element.Id == "res:http://example.org/a");
        Assert.Equal((555d, 666d), (moved.X, moved.Y));
    }

    [Fact]
    public async Task ARepositionLandsInTheRegistration_IsOneUndoAway_AndTheBodyNeverChanges()
    {
        // Arrange.
        var body = CopyFixture("crlf-line-endings.ttl");
        var bodyBytes = await File.ReadAllBytesAsync(body, TestContext.Current.CancellationToken);
        var adp = WriteRegistration("crlf-line-endings.ttl");
        var adpBefore = await File.ReadAllTextAsync(adp, TestContext.Current.CancellationToken);

        // Act.
        await using var session = Open(body, adp);
        var refusal = await session.MoveElementToAsync("res:http://example.org/a", 120, 240, TestContext.Current.CancellationToken);

        // Assert.
        Assert.Equal("", refusal);
        Assert.Equal(new RegistrationPosition(120, 240), RegistrationLayout.Read(adp)["res:http://example.org/a"]);
        // The RDF file never changes by a byte (Requirement 4.1).
        Assert.Equal(bodyBytes, await File.ReadAllBytesAsync(body, TestContext.Current.CancellationToken));
        // And the drag is one undo away, returning the .adp byte for byte (Requirement 4.2).
        await _provider.GetRequiredService<IHistoryStackStore>().Get(_root)
            .UndoAsync(TestContext.Current.CancellationToken);
        Assert.Equal(adpBefore, await File.ReadAllTextAsync(adp, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task TheRefusals_AnswerWithTheirSentences()
    {
        // Arrange.
        var body = CopyFixture("constructs.ttl");

        // Act & assert: a bare file has nowhere to store a position; registering lifts it (4.3).
        await using (var bare = Open(body, null))
        {
            var refusal = await bare.MoveElementToAsync("res:http://example.org/alice", 1, 2, TestContext.Current.CancellationToken);
            Assert.Contains("Register the file", refusal);
        }

        await using var session = Open(body, WriteRegistration("constructs.ttl"));

        // A blank node's identity does not survive a reparse (4.5).
        var blank = await session.MoveElementToAsync("blank:0", 1, 2, TestContext.Current.CancellationToken);
        Assert.Contains("blank node", blank);

        // An edge follows its endpoints.
        var edge = await session.MoveElementToAsync("edge:whatever", 1, 2, TestContext.Current.CancellationToken);
        Assert.Contains("not something this diagram can move", edge);
    }



    [Fact]
    public async Task ADocumentEditWhileNarrowed_NeverResendsWhatTheViewportCulled()
    {
        // Arrange.
        // Found by Agent 2 on timeline and checked here: if the document-changed path renders
        // unfiltered while UpdateView renders filtered, the two disagree about what the client
        // holds, and an ordinary edit silently re-delivers everything the viewport just culled.
        // It is invisible until somebody edits a document while zoomed in.
        var body = CopyFixture("constructs.ttl");
        await using var session = Open(body, WriteRegistration("constructs.ttl"));
        var all = ElementsOf(session).Where(element => element.Type == RdfElementMapper.ResourceType).ToList();
        var first = all.OrderBy(element => element.Y).ThenBy(element => element.X).First();

        var narrowed = session.UpdateView(new DiagramViewport(first.X, first.Y, first.X + 1, first.Y + 1));
        var culled = narrowed.OfType<DiagramRemoveDelta>().SelectMany(delta => delta.ElementIds).ToHashSet(StringComparer.Ordinal);
        Assert.NotEmpty(culled);

        var seen = new List<DiagramDelta>();
        session.Changed += (_, args) => seen.AddRange(args.Deltas);

        // Act.
        // Touch the document: a comment is enough - the reload re-renders and diffs.
        var text = await File.ReadAllTextAsync(body, TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(body, text + Environment.NewLine + "# an edit while the reader is zoomed in" + Environment.NewLine, TestContext.Current.CancellationToken);
        _provider.GetRequiredService<IRdfDocumentStore>().Reload(body);

        // Assert.
        var resent = seen.OfType<DiagramAddDelta>().SelectMany(delta => delta.Elements).Select(element => element.Id);
        Assert.DoesNotContain(resent, id => culled.Contains(id));
    }

    [Fact]
    public async Task NoEdgeIsEverDeliveredWithOneEndMissing()
    {
        // Arrange.
        // Every mapper in this family packs an edge at the default position - (0, 0) - because
        // an edge has no position of its own. A viewport filter that judged edges by position
        // would therefore keep them all while the reader looked at the origin and cull them all
        // on the first pan. Edges are filtered structurally instead: an edge survives only when
        // both of its endpoints did, so a dangling edge cannot be produced.
        var body = CopyFixture("constructs.ttl");
        await using var session = Open(body, WriteRegistration("constructs.ttl"));
        var drawn = ElementsOf(session);
        var anchor = drawn.Where(element => element.Type == RdfElementMapper.ResourceType)
            .OrderBy(element => element.Y).ThenBy(element => element.X).First();

        var held = drawn.Select(element => element.Id).ToHashSet(StringComparer.Ordinal);

        // Act.
        foreach (var viewport in new[]
        {
            new DiagramViewport(anchor.X, anchor.Y, anchor.X + 1, anchor.Y + 1),
            new DiagramViewport(-10_000, -10_000, -9_000, -9_000),
            DiagramViewport.Unbounded,
        })
        {
            foreach (var delta in session.UpdateView(viewport))
            {
                switch (delta)
                {
                    case DiagramAddDelta add:
                        foreach (var element in add.Elements)
                        {
                            held.Add(element.Id);
                        }

                        break;
                    case DiagramRemoveDelta remove:
                        foreach (var id in remove.ElementIds)
                        {
                            held.Remove(id);
                        }

                        break;
                }
            }

            // Assert.
            // An edge id is "res:from|predicate|res:to"; whatever the shape, both endpoints of
            // every edge the connection holds must also be held.
            var edges = held.Where(id => id.StartsWith("edge:", StringComparison.Ordinal)).ToList();
            foreach (var edge in edges)
            {
                var parts = edge["edge:".Length..].Split('|');
                Assert.Contains(parts[0], held);
                Assert.Contains(parts[2], held);
            }
        }
    }

    [Fact]
    public async Task PanningReachesResourcesTheBudgetDiscarded()
    {
        // Arrange.
        // The payoff Requirement 5.4 asks for, and the reason this module was argued from.
        // The drawn-node budget cuts by DOCUMENT ORDER: before viewport filtering, resource
        // 1,001 of a large ontology was unreachable however far the reader panned, because
        // nothing about panning changed which thousand the backend had picked. This is the
        // test that fails if UpdateView merely accepts a viewport and ignores it.
        var beyond = RdfProjection.DefaultBudget + 200;
        var text = new StringBuilder().AppendLine("@prefix ex: <http://example.org/> .");
        for (var i = 0; i < beyond; i++)
        {
            text.AppendLine(string.Create(CultureInfo.InvariantCulture, $"ex:r{i:D5} ex:index \"{i}\" ."));
        }

        var body = IoPath.Combine(_root, "large.ttl");
        await File.WriteAllTextAsync(body, text.ToString(), TestContext.Current.CancellationToken);

        await using var session = Open(body, WriteRegistration("large.ttl"));
        var atOpen = ElementsOf(session)
            .Where(element => element.Type == RdfElementMapper.ResourceType)
            .ToList();

        // The budget still holds the opening view down - that is the floor, and it stays.
        Assert.Equal(RdfProjection.DefaultBudget, atOpen.Count);
        Assert.Contains(atOpen, element => element.Type == RdfElementMapper.ResourceType);
        var drawnAtOpen = atOpen.Select(element => element.Id).ToHashSet(StringComparer.Ordinal);

        // Act.
        // A window over the far end of the layout - where the resources the budget discarded
        // were placed, because the layout is computed over the whole document.
        var lowest = atOpen.Max(element => element.Y);
        var deltas = session.UpdateView(new DiagramViewport(-1000, lowest, 5000, lowest + 100000));

        // Assert.
        var arrived = deltas
            .OfType<DiagramAddDelta>()
            .SelectMany(delta => delta.Elements)
            .Where(element => element.Type == RdfElementMapper.ResourceType)
            .Select(element => element.Id)
            .ToList();

        Assert.NotEmpty(arrived);
        Assert.Contains(arrived, id => !drawnAtOpen.Contains(id));
    }

    [Fact]
    public async Task ADocumentOverTheBudget_KeepsItsBannerAndItsRefusal()
    {
        // Arrange.
        // Viewport filtering must not quietly make a large document editable. The banner and
        // the read-only refusal both come from RdfSelection.IsTruncated, which reads the
        // DOCUMENT and has no viewport to consult - so the drawn set now follows the reader
        // while what the document may do stays exactly as it was.
        var beyond = RdfProjection.DefaultBudget + 200;
        var text = new StringBuilder().AppendLine("@prefix ex: <http://example.org/> .");
        for (var i = 0; i < beyond; i++)
        {
            text.AppendLine(string.Create(CultureInfo.InvariantCulture, $"ex:r{i:D5} ex:index \"{i}\" ."));
        }

        var body = IoPath.Combine(_root, "large.ttl");
        await File.WriteAllTextAsync(body, text.ToString(), TestContext.Current.CancellationToken);

        await using var session = Open(body, WriteRegistration("large.ttl"));

        // Act.
        var atOpen = ElementsOf(session);
        var narrowed = session.UpdateView(new DiagramViewport(0, 0, 300, 200));

        // Assert.
        Assert.Contains(atOpen, element => element.Type == RdfElementMapper.TruncationType);
        var afterwards = narrowed.OfType<DiagramRemoveDelta>().SelectMany(delta => delta.ElementIds);
        Assert.DoesNotContain(RdfElementMapper.TruncationId, afterwards);
    }

    [Fact]
    public async Task AViewportChange_AddsWhatCameIntoViewAndRemovesWhatLeft()
    {
        // Arrange.
        // The behavioural definition of the whole mechanism (view-delta-adoption Requirement
        // 1.3): a view CHANGE produces deltas. This is the test that fails against a session
        // whose UpdateView returns an empty list, which no client-side assertion can catch.
        var body = CopyFixture("constructs.ttl");
        await using var session = Open(body, WriteRegistration("constructs.ttl"));
        var all = ElementsOf(session).Where(element => element.Type == RdfElementMapper.ResourceType).ToList();
        Assert.True(all.Count >= 2, "the fixture must hold at least two resources for this to mean anything");

        var first = all.OrderBy(element => element.Y).ThenBy(element => element.X).First();

        // Act.
        // A window tight around one node's own cell, then the whole plane again.
        var narrowed = session.UpdateView(new DiagramViewport(first.X, first.Y, first.X + 1, first.Y + 1));
        var widened = session.UpdateView(DiagramViewport.Unbounded);

        // Assert.
        var removed = narrowed.OfType<DiagramRemoveDelta>().SelectMany(delta => delta.ElementIds).ToHashSet(StringComparer.Ordinal);
        Assert.NotEmpty(removed);
        Assert.DoesNotContain(first.Id, removed);

        var restored = widened.OfType<DiagramAddDelta>().SelectMany(delta => delta.Elements).Select(element => element.Id).ToHashSet(StringComparer.Ordinal);
        Assert.Subset(restored, removed);
    }

    [Fact]
    public async Task AViewportChange_EmitsAddBeforeRemove()
    {
        // Arrange.
        // The order the two reference implementations emit, and the order this module's own
        // mapper already produced. view-delta-adoption Requirement 4.3 anticipated the
        // opposite; the code is what the client has always been given.
        var body = CopyFixture("constructs.ttl");
        await using var session = Open(body, WriteRegistration("constructs.ttl"));
        var all = ElementsOf(session).Where(element => element.Type == RdfElementMapper.ResourceType).ToList();
        var first = all.OrderBy(element => element.Y).ThenBy(element => element.X).First();

        session.UpdateView(new DiagramViewport(first.X, first.Y, first.X + 1, first.Y + 1));

        // Act.
        var deltas = session.UpdateView(DiagramViewport.Unbounded);

        // Assert.
        Assert.Contains(deltas, delta => delta is DiagramAddDelta);
        var kinds = deltas.Select(delta => delta is DiagramAddDelta ? "add" : "remove").ToList();
        Assert.Equal(kinds.OrderBy(kind => kind == "add" ? 0 : 1).ToList(), kinds);
    }

    [Fact]
    public async Task AnUnchangedViewport_SaysNothingTwice()
    {
        // Arrange.
        // A settled view that has not moved is not news. Without this a canvas that re-reports
        // the same rectangle - which the debounce permits on any re-render - would re-send the
        // whole diagram.
        var body = CopyFixture("constructs.ttl");
        await using var session = Open(body, WriteRegistration("constructs.ttl"));

        // Act.
        session.UpdateView(DiagramViewport.Unbounded);
        var again = session.UpdateView(DiagramViewport.Unbounded);

        // Assert.
        Assert.Empty(again);
    }

    [Fact]
    public async Task PanningDoesNotMoveTheNodesItBringsIntoView()
    {
        // Arrange.
        // The layout is computed over the whole document and only then filtered. Computed over
        // the visible set instead, the bands would pack by whatever the viewport admitted and
        // every node would shift as the reader panned - the diagram would crawl.
        var body = CopyFixture("constructs.ttl");
        await using var session = Open(body, WriteRegistration("constructs.ttl"));
        var atOpen = ElementsOf(session).ToDictionary(element => element.Id, element => (element.X, element.Y), StringComparer.Ordinal);

        // Act.
        session.UpdateView(new DiagramViewport(0, 0, 1, 1));
        var readmitted = session.UpdateView(DiagramViewport.Unbounded)
            .OfType<DiagramAddDelta>()
            .SelectMany(delta => delta.Elements)
            .ToList();

        // Assert.
        Assert.NotEmpty(readmitted);
        foreach (var element in readmitted)
        {
            Assert.Equal(atOpen[element.Id], (element.X, element.Y));
        }
    }

    [Fact]
    public async Task ADocumentWithinTheBudget_ShowsNoBannerWhateverTheViewport()
    {
        // Arrange.
        // Viewport filtering is not truncation. The banner and the read-only refusal both come
        // from RdfSelection.IsTruncated, which reads the DOCUMENT and has no viewport to
        // consult - so a banner raised by a narrow view would contradict it, leaving a file
        // that says it is truncated while every edit is allowed.
        var body = CopyFixture("constructs.ttl");
        await using var session = Open(body, WriteRegistration("constructs.ttl"));

        // Act.
        session.UpdateView(new DiagramViewport(0, 0, 1, 1));
        var elements = ElementsOf2(session);

        // Assert.
        Assert.DoesNotContain(elements, element => element.Type == RdfElementMapper.TruncationType);
    }

    /// <summary>What the connection holds now: the baseline is spent, so this re-reads the view.</summary>
    private static IReadOnlyList<DiagramElement> ElementsOf2(IDiagramSession session)
    {
        var deltas = session.UpdateView(DiagramViewport.Unbounded);
        return deltas.OfType<DiagramAddDelta>().SelectMany(delta => delta.Elements).ToList();
    }
}
