using System.Text;
using System.Text.RegularExpressions;
using EtAlii.Adp.Diagram.Rdf.Tests.Parity;
using Xunit;
using Path = System.IO.Path;

namespace EtAlii.Adp.Diagram.Rdf.Tests;

/// <summary>
/// An <c>.nt</c> body is written as N-Triples (rdf-disl-fbl task 12, Requirement 5.5, finding B7): a triple added to a
/// subject that already has one is a full statement line of its own, not a <c>;</c> continuation, and <c>rdf:type</c> is
/// written as its IRI, not as Turtle's <c>a</c>. A <c>.ttl</c> body is written as before.
/// </summary>
public partial class RdfWriterNTriplesTests
{
    private const string Alice = "http://example.org/alice";
    private const string Label = "http://www.w3.org/2000/01/rdf-schema#label";

    private static string Fixture => Path.Combine("diagrams", "rdf", "backend", "EtAlii.Adp.Diagram.Rdf.Tests", "Fixtures", "simple.nt");

    /// <summary>One N-Triples statement: a subject, a predicate and an object, each written in full, and the <c>.</c>.</summary>
    [GeneratedRegex("""^(<[^<>"{}|^`\\ ]*>|_:\S+) <[^<>"{}|^`\\ ]*> (<[^<>"{}|^`\\ ]*>|_:\S+|"([^"\\]|\\.)*"(@[A-Za-z]+(-[A-Za-z0-9]+)*|\^\^<[^<>"{}|^`\\ ]*>)?) \.$""")]
    private static partial Regex Statement();

    private static void AssertNTriples(string text) =>
        Assert.All(text.Split('\n').Select(line => line.TrimEnd('\r')).Where(line => line.Length > 0), line => Assert.Matches(Statement(), line));

    [Fact]
    public async Task ATripleAddedToASubjectOfAnNtBody_IsAFullStatementLine_AndTheBodyStaysNTriples()
    {
        // Arrange: alice already has three statements in the fixture, whose lines end in LF.
        await using var host = new RdfHost();
        var path = host.Copy(Fixture);
        var before = RdfHost.Bytes(path);
        AssertNTriples(Encoding.UTF8.GetString(before));

        // Act.
        var result = await host.History.ExecuteAsync(new AddRdfTripleCommand(path, Alice, Label, "", "Alice Liddell"), TestContext.Current.CancellationToken);

        // Assert: the new statement is a line of its own after alice's last one, and nothing else changed.
        Assert.True(result.IsSuccess, result.Error);
        var text = Encoding.UTF8.GetString(RdfHost.Bytes(path));
        AssertNTriples(text);
        var lines = Encoding.UTF8.GetString(before).Split("\n").ToList();
        lines.Insert(3, $"<{Alice}> <{Label}> \"Alice Liddell\" .");
        Assert.Equal(string.Join("\n", lines), text);

        // And one undo gives the bytes back.
        Assert.True((await host.History.UndoAsync(TestContext.Current.CancellationToken)).IsSuccess);
        Assert.Equal(before, RdfHost.Bytes(path));
    }

    [Fact]
    public async Task ATypeAddedToAnNtBody_IsWrittenAsItsIri()
    {
        // Arrange.
        await using var host = new RdfHost();
        var path = host.Copy(Fixture);

        // Act: a new resource, as the canvas adds one, and a type for an existing subject.
        var added = await host.History.ExecuteAsync(new AddRdfTripleCommand(path, "http://example.org/parity", RdfVocabulary.Type, RdfVocabulary.Resource), TestContext.Current.CancellationToken);
        var typed = await host.History.ExecuteAsync(new AddRdfTripleCommand(path, Alice, RdfVocabulary.Type, "http://xmlns.com/foaf/0.1/Agent"), TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(added.IsSuccess, added.Error);
        Assert.True(typed.IsSuccess, typed.Error);
        var text = Encoding.UTF8.GetString(RdfHost.Bytes(path));
        AssertNTriples(text);
        Assert.Contains($"<http://example.org/parity> <{RdfVocabulary.Type}> <{RdfVocabulary.Resource}> .", text, StringComparison.Ordinal);
        Assert.Contains($"<{Alice}> <{RdfVocabulary.Type}> <http://xmlns.com/foaf/0.1/Agent> .", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ATripleAddedToASubjectOfATtlBody_IsStillAContinuationOfItsStatement()
    {
        // Arrange: the same statements, in a Turtle file.
        await using var host = new RdfHost();
        var path = host.Write("simple.ttl", RdfHost.Bytes(host.Copy(Fixture)));

        // Act.
        var result = await host.History.ExecuteAsync(new AddRdfTripleCommand(path, Alice, Label, "", "Alice Liddell"), TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(result.IsSuccess, result.Error);
        var text = Encoding.UTF8.GetString(RdfHost.Bytes(path));
        Assert.Contains($"<{Alice}> <http://xmlns.com/foaf/0.1/knows> _:b0 ;", text, StringComparison.Ordinal);
        Assert.Contains($"    <{Label}> \"Alice Liddell\" .", text, StringComparison.Ordinal);
    }
}
