using EtAlii.Adp.Context;
using EtAlii.Adp.Context.Wire;
using EtAlii.Adp.Diagram;
using EtAlii.Adp.Diagram.Rdf;
using EtAlii.Adp.Diagram.Wire;
using EtAlii.Adp.Documents.Wire;
using EtAlii.Adp.TestSupport.Host;
using Xunit;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Backend.Tests;

/// <summary>
/// The RDF arc over the real host: a registered Turtle file streaming cards and edges, a bare
/// file routing on sight, a reposition landing in the <c>.adp</c>'s <c>layout:</c> block while
/// the RDF file never changes by a byte, a label edit travelling the whole selection chain, and
/// two registrations sharing one document (rdf-diagram Requirements 2 and 4, and the
/// integration halves of 1 and 5).
/// </summary>
public class RdfFlowTests(HostFixture host) : IClassFixture<HostFixture>
{
    private const string CurieId = "res:http://example.org/curie";

    private const string Curie = """
        @prefix ex: <http://example.org/> .
        @prefix rdfs: <http://www.w3.org/2000/01/rdf-schema#> .

        ex:curie a ex:Scientist ;
            rdfs:label "Marie Curie" ;
            ex:discovered ex:radium, ex:polonium .

        ex:radium rdfs:label "Radium" .
        """;

    private readonly string _projectFolder = Seed(host.NewProjectFolder());

    private static string Seed(string projectFolder)
    {
        // One body, two registrations - and one bare file with no registration at all.
        File.WriteAllText(IoPath.Combine(projectFolder, "curie.ttl"), Curie);
        File.WriteAllText(IoPath.Combine(projectFolder, "curie.adp"), "w3c/rdf\nbody: curie.ttl\n");
        File.WriteAllText(IoPath.Combine(projectFolder, "curie.second.adp"), "w3c/rdf\nbody: curie.ttl\n");
        File.WriteAllText(IoPath.Combine(projectFolder, "bare.ttl"), Curie);
        return projectFolder;
    }

    [Fact]
    public async Task ARegisteredFile_StreamsCardsAndEdges_AndABareOne_RoutesOnSight()
    {
        // Arrange: Requirement 2.2 - a bare .ttl needs no ceremony, and a registered one opens
        // its reading like any arranged module's.
        using var session = await FlowSession.OpenAsync(host, _projectFolder, TestContext.Current.CancellationToken);

        // Act.
        var registered = await DiagramFlow.BaselineAsync(session, "curie.adp");
        var bare = await DiagramFlow.BaselineAsync(session, "bare.ttl");

        // Assert: cards for the IRI terms, the discovered edges, the literal folded away.
        Assert.Contains(registered, element => element.Id.Value == CurieId && element.Type == "w3c/rdf+resource");
        Assert.Contains(registered, element => element.Id.Value == "res:http://example.org/radium");
        Assert.Contains(registered, element =>
            element.Id.Value == $"edge:{CurieId}|http://example.org/discovered|res:http://example.org/radium");
        Assert.DoesNotContain(registered, element => element.Id.Value.Contains("Marie", StringComparison.Ordinal));

        Assert.Contains(bare, element => element.Id.Value == CurieId);
    }

    [Fact]
    public async Task AReposition_LandsInTheAdp_SurvivesReopen_AndUndoReturnsIt_WithTheBodyUntouchedThroughout()
    {
        // Arrange: the layout-persistence promise (Requirement 4), end to end. The diagram must
        // be open on the SAME connection, because core will not edit a document a caller is not
        // looking at.
        using var session = await FlowSession.OpenAsync(host, _projectFolder, TestContext.Current.CancellationToken);
        var client = new DiagramService.DiagramServiceClient(session.Channel);
        var watchId = ShortGuid.NewShortGuid();
        var bodyPath = IoPath.Combine(_projectFolder, "curie.ttl");
        var adpPath = IoPath.Combine(_projectFolder, "curie.adp");
        var bodyBefore = await File.ReadAllBytesAsync(bodyPath, TestContext.Current.CancellationToken);
        var adpBefore = await File.ReadAllTextAsync(adpPath, TestContext.Current.CancellationToken);

        using var cts = FlowSession.CreateMessageTimeout(TestContext.Current.CancellationToken);
        using var call = client.Open(
            new OpenDiagramRequest { ProjectId = session.ProjectId, WatchId = watchId, Path = DiagramFlow.PathOf("curie.adp") },
            session.Headers, cancellationToken: cts.Token);
        await call.ResponseStream.ReadAddAsync(cts.Token);

        // Act: drag the card to an authored spot.
        var moved = await client.MoveElementAsync(
            new MoveElementRequest
            {
                ProjectId = session.ProjectId,
                WatchId = watchId,
                Path = DiagramFlow.PathOf("curie.adp"),
                ElementId = CurieId,
                Position = new Point2D { X = 300, Y = 180 },
            },
            session.Headers, cancellationToken: TestContext.Current.CancellationToken);

        // Assert: the position is in the registration, and nowhere near the RDF (4.1).
        Assert.Equal("", moved.Error);
        Assert.Equal(new RegistrationPosition(300, 180), RegistrationLayout.Read(adpPath)[CurieId]);
        Assert.Equal(bodyBefore, await File.ReadAllBytesAsync(bodyPath, TestContext.Current.CancellationToken));

        // Act, continued: a fresh open sees the stored position overlaid (4.2).
        var reopened = await DiagramFlow.BaselineAsync(session, "curie.adp");
        var card = reopened.Single(element => element.Id.Value == CurieId);
        Assert.Equal((300d, 180d), (card.Position.X, card.Position.Y));

        // Act, continued: one undo returns the registration byte for byte (4.2).
        var undone = await DiagramFlow.ExecuteProjectActionAsync(session, watchId, HistoryContextActionProvider.UndoActionId);

        // Assert.
        Assert.True(undone.Accepted, undone.Error);
        Assert.Equal(adpBefore, await File.ReadAllTextAsync(adpPath, TestContext.Current.CancellationToken));
        Assert.Equal(bodyBefore, await File.ReadAllBytesAsync(bodyPath, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ALabelEdit_TravelsTheSelectionChain_RewritesOneToken_AndIsOneUndoAway()
    {
        // Arrange: the property path over the real selection chain - which also proves an RDF
        // element resolves while every other module's resolver is registered beside this one.
        using var session = await FlowSession.OpenAsync(host, _projectFolder, TestContext.Current.CancellationToken);
        var contextClient = session.Context;
        var watchId = ShortGuid.NewShortGuid();
        var entries = await DiagramFlow.EntriesAsync(session, watchId);
        var bodyPath = IoPath.Combine(_projectFolder, "curie.ttl");
        var before = await File.ReadAllTextAsync(bodyPath, TestContext.Current.CancellationToken);

        var selected = await contextClient.SelectAsync(
            new SelectRequest
            {
                ProjectId = session.ProjectId,
                WatchId = watchId,
                Selection = DiagramFlow.ElementChain(entries["curie.adp"], CurieId),
            },
            session.Headers, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Empty(selected.Error);

        // Act.
        var result = await contextClient.SetPropertyAsync(
            new SetPropertyRequest
            {
                ProjectId = session.ProjectId,
                WatchId = watchId,
                PropertyId = RdfContextPropertyProvider.LabelProperty,
                Value = "Curie, Marie",
            },
            session.Headers, cancellationToken: TestContext.Current.CancellationToken);

        // Assert: exactly the literal token was rewritten, keeping the author's abbreviations.
        Assert.True(result.Accepted, result.Error);
        var edited = await File.ReadAllTextAsync(bodyPath, TestContext.Current.CancellationToken);
        Assert.Contains("rdfs:label \"Curie, Marie\" ;", edited, StringComparison.Ordinal);
        Assert.Contains("ex:discovered ex:radium, ex:polonium .", edited, StringComparison.Ordinal);

        // Act, continued: one undo returns the RDF byte for byte.
        var undone = await DiagramFlow.ExecuteProjectActionAsync(session, watchId, HistoryContextActionProvider.UndoActionId);

        // Assert.
        Assert.True(undone.Accepted, undone.Error);
        Assert.Equal(before, await File.ReadAllTextAsync(bodyPath, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task TwoRegistrationsOnOneBody_ShareTheEditedDocument()
    {
        // Arrange: Requirement 2.4 - one store, one document, one history; an edit through one
        // registration is visible through the other.
        using var session = await FlowSession.OpenAsync(host, _projectFolder, TestContext.Current.CancellationToken);
        var contextClient = session.Context;
        var watchId = ShortGuid.NewShortGuid();
        var entries = await DiagramFlow.EntriesAsync(session, watchId);

        var selected = await contextClient.SelectAsync(
            new SelectRequest
            {
                ProjectId = session.ProjectId,
                WatchId = watchId,
                Selection = DiagramFlow.ElementChain(entries["curie.adp"], "res:http://example.org/radium"),
            },
            session.Headers, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Empty(selected.Error);

        // Act: edit through the first registration…
        var result = await contextClient.SetPropertyAsync(
            new SetPropertyRequest
            {
                ProjectId = session.ProjectId,
                WatchId = watchId,
                PropertyId = RdfContextPropertyProvider.LabelProperty,
                Value = "Radium (Ra)",
            },
            session.Headers, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.Accepted, result.Error);

        // …and open the second: the same document, already carrying the edit.
        var second = await DiagramFlow.BaselineAsync(session, "curie.second.adp");

        // Assert.
        Assert.Contains(second, element => element.Id.Value == "res:http://example.org/radium");
        var payload = second.Single(element => element.Id.Value == "res:http://example.org/radium");
        var decoded = RdfResourcePayload.Parser.ParseFrom(payload.Payload.Value);
        Assert.Contains(decoded.Rows, row => row.Value == "Radium (Ra)");
    }
}
