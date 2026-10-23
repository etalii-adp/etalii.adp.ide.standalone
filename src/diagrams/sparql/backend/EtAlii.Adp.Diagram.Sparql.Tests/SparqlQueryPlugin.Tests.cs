using System.Text;
using EtAlii.Adp.Diagram.Sparql.Tests.Parity;
using EtAlii.Adp.Specification.Fbl;
using EtAlii.Adp.Specification.Fbl.Planning;
using EtAlii.Adp.Specification.Fbl.Plugins;
using Xunit;

namespace EtAlii.Adp.Diagram.Sparql.Tests;

/// <summary>
/// The persistence plugin of <c>sparql.fbl#query</c> (sparql-disl-fbl, task 9): the records it reads and the UTF-8 byte
/// spans they carry (criterion 3.3), a reading that never throws on content, its one <c>std.unparseable</c>, and a plan
/// refused for every kind of change.
/// </summary>
public class SparqlQueryPluginTests
{
    /// <summary>A comment that puts characters of two, three and four UTF-8 bytes before everything a query states.</summary>
    private const string Wide = "# café ☃ 😀\r\n";

    private static readonly SparqlQueryPlugin _plugin = new();

    private static PluginReadResult Read(byte[] bytes) => _plugin.Read(new PluginReadRequest([new PluginFile("query.rq", bytes)]));

    private static string Slice(byte[] bytes, Span span) => Encoding.UTF8.GetString(bytes, span.Start, span.End - span.Start);

    public static TheoryData<string> Corpus() => [.. SparqlCorpus.Documents()];

    [Theory]
    [MemberData(nameof(Corpus))]
    public void EachDocument_IsReadAsTheRecordsTheModuleBuilds_WithTheSameSpans(string relative)
    {
        // Arrange.
        var bytes = File.ReadAllBytes(SparqlCorpus.Full(relative));
        var text = Encoding.UTF8.GetString(bytes);

        // Act.
        var read = Read(bytes);

        // Assert: a document the parser reads is the module's records, span for span; the one it cannot read is none.
        SparqlQueryModel model;
        try
        {
            model = SparqlParser.Parse(text);
        }
        catch (SparqlParseException)
        {
            Assert.True(read.Unreadable);
            Assert.Empty(read.Elements);
            return;
        }

        var expected = SparqlRecords.From(model, text).Elements;
        Assert.False(read.Unreadable);
        Assert.Empty(read.Findings);
        Assert.Equal(
            expected.Select(record => (record.Id, record.Type, record.OwnSpan, record.Line)),
            read.Elements.Select(record => (record.Id, record.Type, record.OwnSpan, record.Line)));
    }

    [Theory]
    [MemberData(nameof(Corpus))]
    public void EverySpan_SlicedFromTheBytes_IsTheEntrysText_WithWideCharactersBeforeIt(string relative)
    {
        // Arrange: the document as it is, and the same document after a comment of wide characters. A span counted in
        // characters would slice the second three bytes short of its entry for every entry after the comment.
        var narrow = File.ReadAllBytes(SparqlCorpus.Full(relative));
        var text = Encoding.UTF8.GetString(narrow);
        if (Read(narrow).Unreadable)
        {
            return;
        }

        var wide = Encoding.UTF8.GetBytes(Wide + text);

        // Act.
        var before = Read(narrow).Elements;
        var after = Read(wide).Elements;

        // Assert: every entry slices to the same text, the query's own record to the whole document.
        Assert.Equal(before.Count, after.Count);
        Assert.Equal(Wide + text, Slice(wide, after[0].OwnSpan));
        Assert.Equal(
            before.Skip(1).Select(record => (record.Id, Slice(narrow, record.OwnSpan), record.Line + 1)),
            after.Skip(1).Select(record => (record.Id, Slice(wide, record.OwnSpan), record.Line)));
    }

    [Fact]
    public void ASpan_CountsTheBytesOfTheCharactersWrittenBeforeIt_NotTheCharacters()
    {
        // Arrange: a literal of a two-byte character, before a pattern and a filter on the line after it.
        const string Query = "PREFIX ex: <http://example.org/>\r\n"
            + "SELECT ?s WHERE {\r\n"
            + "  ?s ex:name \"café\" .\r\n"
            + "  ?s ex:p ?o .\r\n"
            + "  FILTER(?o != \"ø\")\r\n"
            + "}\r\n";
        var bytes = Encoding.UTF8.GetBytes(Query);

        // Act.
        var records = Read(bytes).Elements;

        // Assert.
        Assert.Equal(
            ["PREFIX ex: <http://example.org/>", "?s ex:name \"café\"", "?s ex:p ?o", "FILTER(?o != \"ø\")"],
            records.Skip(1).Select(record => Slice(bytes, record.OwnSpan)));
        Assert.Equal(new Span(0, bytes.Length), records[0].OwnSpan);
    }

    [Theory]
    [MemberData(nameof(Corpus))]
    public void ReadingAnyPrefixOfADocument_NeverThrows(string relative)
    {
        // Arrange.
        var bytes = File.ReadAllBytes(SparqlCorpus.Full(relative));

        // Act & assert: every cut, including one inside a multi-byte character of the wide variant.
        foreach (var document in new[] { bytes, Encoding.UTF8.GetBytes(Wide + Encoding.UTF8.GetString(bytes)) })
        {
            for (var length = 0; length <= document.Length; length++)
            {
                var read = Read(document[..length]);
                Assert.True(read.Unreadable ? read.Elements.Count == 0 && read.Findings.Count == 1 : read.Findings.Count == 0, $"A prefix of {length} bytes of {relative}.");
            }
        }
    }

    [Fact]
    public void AnUnparseableBody_IsUnreadable_WithOneUnparseableFinding_AtTheLineAndColumnTheParserStopped()
    {
        // Arrange.
        var bytes = File.ReadAllBytes(SparqlCorpus.Full("diagrams/sparql/backend/EtAlii.Adp.Diagram.Sparql.Tests/Fixtures/broken.rq"));
        var expected = Assert.Throws<SparqlParseException>(() => SparqlParser.Parse(Encoding.UTF8.GetString(bytes)));

        // Act.
        var read = Read(bytes);

        // Assert.
        Assert.True(read.Unreadable);
        Assert.Empty(read.Elements);
        var finding = Assert.Single(read.Findings);
        Assert.Equal((FindingCodes.Unparseable, FindingSeverity.Error, expected.Message), (finding.Code, finding.Severity, finding.Message));
        Assert.Equal((4, expected.Column), (finding.Location!.Line, finding.Location.Column));
        Assert.Equal(expected.Line, finding.Location.Line);
    }

    public static TheoryData<ModelChange> Changes() =>
    [
        new ModelChange.Add("Pattern", null, new Dictionary<string, object?>()),
        new ModelChange.Move("pattern-0", null, 0),
        new ModelChange.Retype("pattern-0", "Constraint", new Dictionary<string, object?>()),
        new ModelChange.Set("pattern-0", new Dictionary<string, object?> { ["predicate"] = "ex:q" }),
        new ModelChange.Remove("pattern-0"),
        new ModelChange.Place("pattern-0", 1, 2),
        new ModelChange.Identify("var:s", "pattern-0"),
        new ModelChange.StoreId("pattern-0", "pattern-9"),
        new ModelChange.Setting("layout", "pattern-0", "1 2"),
        new ModelChange.Save(),
    ];

    [Theory]
    [MemberData(nameof(Changes))]
    public void EveryKindOfChange_IsRefused_WithTheBindingsReadOnlySentence(ModelChange change)
    {
        // Arrange.
        var files = new[] { new PluginFile("query.rq", Encoding.UTF8.GetBytes("SELECT ?s WHERE { ?s ?p ?o }")) };

        // Act.
        var plan = _plugin.Plan(new PluginPlanRequest(files, _plugin.Read(new PluginReadRequest(files)), change));

        // Assert.
        var refused = Assert.IsType<PluginPlanResult.Refused>(plan);
        Assert.Equal("A query diagram offers no edits: it reads the .rq file, which is edited in a text editor.", refused.Reason);
    }

    [Fact]
    public void EveryKindOfChange_IsCovered()
    {
        // Arrange: a kind of change added to FBL is a kind this plugin has not been seen to refuse.
        var kinds = typeof(ModelChange).GetNestedTypes().Where(type => type.IsSubclassOf(typeof(ModelChange))).Select(type => type.Name).Order(StringComparer.Ordinal);

        // Act & assert.
        Assert.Equal(kinds, Changes().Select(row => row.Data.GetType().Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void TheTemplate_IsTheBindingsText_AndTheIdAndWatchAreTheBindings()
    {
        // Act.
        var template = _plugin.Template(new PluginTemplateRequest("query", new Dictionary<string, string>()));

        // Assert.
        Assert.Equal(SparqlBody.Binding.Template!.Text, Encoding.UTF8.GetString(template));
        Assert.Equal(SparqlBody.Binding.Plugin!.Plugin, _plugin.Id);
        Assert.Empty(_plugin.Watch(Read(template)));
    }
}
