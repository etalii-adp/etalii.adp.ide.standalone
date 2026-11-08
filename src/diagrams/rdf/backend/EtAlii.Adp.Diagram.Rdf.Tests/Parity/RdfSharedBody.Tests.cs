using Xunit;
using Path = System.IO.Path;

namespace EtAlii.Adp.Diagram.Rdf.Tests.Parity;

/// <summary>
/// Four readings of one file share one body (rdf-disl-fbl task 7, Requirement 7.2): the RDF graph and the OWL
/// ontology of one file read one <see cref="RdfBody"/>, an edit made while one is open shows in the other at once and
/// in what the plugin reads, and an undo, from whichever reading, undoes the body's most recent edit.
/// </summary>
public class RdfSharedBodyTests
{
    private const string Pizza = "http://example.org/pizza#";
    private const string OwlClass = "http://www.w3.org/2002/07/owl#Class";
    private const string RdfType = "http://www.w3.org/1999/02/22-rdf-syntax-ns#type";

    private static string Fixture => Path.Combine("diagrams", "rdf", "backend", "EtAlii.Adp.Diagram.Rdf.Tests", "Fixtures", "owl-ontology.ttl");

    [Fact]
    public async Task AnEditUnderOneReading_ShowsUnderAnother_AndInWhatThePluginReads()
    {
        // Arrange.
        await using var host = new RdfHost();
        var path = host.Copy(Fixture);
        var graph = host.Open(ServiceCollectionAddRdfExtension.RdfOrigin, path);
        var ontology = host.Open(ServiceCollectionAddRdfExtension.OwlOrigin, path);
        Assert.DoesNotContain(await IdsAsync(ontology), id => id.Contains("Calzone", StringComparison.Ordinal));
        var before = host.Store.Get(path)!.Document;

        // Act: a class declared from the graph's menu, as one command of the project's history.
        var result = await host.History.ExecuteAsync(new AddRdfTripleCommand(path, Pizza + "Calzone", RdfType, OwlClass), TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(result.IsSuccess, result.Error);
        var body = host.Store.Get(path)!.Document;
        Assert.NotSame(before, body);
        Assert.Contains(await IdsAsync(graph), id => id.Contains("Calzone", StringComparison.Ordinal));
        Assert.Contains(await IdsAsync(ontology), id => id.Contains("Calzone", StringComparison.Ordinal));
        Assert.Contains(body.Reading.Elements, element => element.Type == "Triple" && (string?)element.Attributes["s"] == Pizza + "Calzone" && (string?)element.Attributes["o"] == OwlClass);
    }

    [Fact]
    public async Task AnUndo_UndoesTheBodysMostRecentEdit_WhicheverReadingMadeIt()
    {
        // Arrange: one edit while the graph is open, then one while the ontology is.
        await using var host = new RdfHost();
        var path = host.Copy(Fixture);
        var graph = host.Open(ServiceCollectionAddRdfExtension.RdfOrigin, path);
        await host.Store.GetOrLoadAsync(path, TestContext.Current.CancellationToken);
        Assert.True((await host.History.ExecuteAsync(new AddRdfTripleCommand(path, Pizza + "Calzone", RdfType, OwlClass), TestContext.Current.CancellationToken)).IsSuccess);
        var afterFirst = RdfHost.Bytes(path);
        var ontology = host.Open(ServiceCollectionAddRdfExtension.OwlOrigin, path);
        Assert.True((await host.History.ExecuteAsync(new AddRdfTripleCommand(path, Pizza + "Stromboli", RdfType, OwlClass), TestContext.Current.CancellationToken)).IsSuccess);

        // Act.
        var undone = await host.History.UndoAsync(TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(undone.IsSuccess, undone.Error);
        Assert.Equal(afterFirst, RdfHost.Bytes(path));
        foreach (var session in (Sessions.IDiagramSession[])[graph, ontology])
        {
            var ids = await IdsAsync(session);
            Assert.Contains(ids, id => id.Contains("Calzone", StringComparison.Ordinal));
            Assert.DoesNotContain(ids, id => id.Contains("Stromboli", StringComparison.Ordinal));
        }

        Assert.DoesNotContain(host.Store.Get(path)!.Document.Reading.Elements, element => element.Type == "Triple" && (string?)element.Attributes["s"] == Pizza + "Stromboli");
    }

    private static async Task<IReadOnlyList<string>> IdsAsync(Sessions.IDiagramSession session) =>
        [.. (await RdfHost.DrawnAsync(session, TestContext.Current.CancellationToken)).Select(element => element.Id)];
}
