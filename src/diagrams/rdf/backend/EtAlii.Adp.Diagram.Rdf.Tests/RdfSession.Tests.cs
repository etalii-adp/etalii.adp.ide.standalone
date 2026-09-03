using EtAlii.Adp.Backend;
using EtAlii.Adp.Backend.Diagrams;
using EtAlii.Adp.Backend.Hierarchy;
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
            .AddCommands()
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
        var factory = _provider.GetServices<IDiagramSessionFactory>().Single();
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
        var bodyBytes = File.ReadAllBytes(body);
        var adp = WriteRegistration("crlf-line-endings.ttl");
        var adpBefore = File.ReadAllText(adp);

        // Act.
        await using var session = Open(body, adp);
        var refusal = await session.MoveElementToAsync("res:http://example.org/a", 120, 240, TestContext.Current.CancellationToken);

        // Assert.
        Assert.Equal("", refusal);
        Assert.Equal(new RegistrationPosition(120, 240), RegistrationLayout.Read(adp)["res:http://example.org/a"]);
        // The RDF file never changes by a byte (Requirement 4.1).
        Assert.Equal(bodyBytes, File.ReadAllBytes(body));
        // And the drag is one undo away, returning the .adp byte for byte (Requirement 4.2).
        await _provider.GetRequiredService<IHistoryStackStore>().Get(_root)
            .UndoAsync(TestContext.Current.CancellationToken);
        Assert.Equal(adpBefore, File.ReadAllText(adp));
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
}
