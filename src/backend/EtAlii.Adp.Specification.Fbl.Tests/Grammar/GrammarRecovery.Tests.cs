using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using EtAlii.Adp.Specification.Fbl.Documents;
using EtAlii.Adp.Specification.Fbl.Grammar;
using EtAlii.Adp.Specification.Fbl.History;
using EtAlii.Adp.Specification.Fbl.Planning;
using EtAlii.Adp.Specification.Fbl.Text;
using Xunit;

namespace EtAlii.Adp.Specification.Fbl.Tests.Grammar;

/// <summary>
/// Reading a body of FBL 0.9's grammar family in part (FBL §4.9, <i>Reading in part</i>, §7.4, §7.5; etalii.adp spec 072,
/// Requirement 5): without <c>recover</c> a body that does not read is unreadable at the failing token; with it the body is
/// read unit by unit, the text that does not read kept, bound to nothing and reported once where reading stopped.
/// </summary>
public class GrammarRecoveryTests
{
    /// <summary>Statements of a key and a quoted value ending in a full stop, one per line, with comments, read statement by statement.</summary>
    private const string Statements = "start: statement*\n?statement: entry\n          | _NL\nentry: KEY _SEP VALUE \".\"\n_SEP: \":\"\nKEY: /[a-z]+/\nVALUE: /\"[^\"\\n]*\"/\n_NL: /\\n/\nWS: /[ \\t]+/\nCOMMENT: /#[^\\n]*/\n%ignore WS\n%ignore COMMENT\n";

    private const string Recover = """{ "unit": "statement", "sync": ["."] }""";

    [Fact]
    public void ABodyThatReads_ReadsEveryStatementWithoutAFinding()
    {
        // Act.
        var model = Read("a: \"one\".\nb: \"two\".\n", Recover);

        // Assert.
        Assert.Equal(["a", "b"], model.Elements.Select(e => e.Id));
        Assert.Empty(model.Findings);
    }

    [Theory]
    [InlineData("a: \"one\".\nb: \"two.\nc: \"three\".\n", 2, 4)]
    [InlineData("a: \"one\".\nb: ?? \"two\".\nc: \"three\".\n", 2, 4)]
    public void WithoutRecover_ABodyThatDoesNotRead_IsUnreadableAtTheFailingToken(string body, int line, int column)
    {
        // Act.
        var model = Read(body, recover: null);

        // Assert: an unclosed string and text no terminal matches, each where the parser met it (FBL §7.5).
        Assert.True(model.Unreadable);
        var finding = Assert.Single(model.Findings);
        Assert.Equal(FindingCodes.Unparseable, finding.Code);
        Assert.Equal((line, column), (finding.Location!.Line, finding.Location.Column));
    }

    [Fact]
    public void AnUnclosedString_IsSkippedToTheEndOfItsStatement()
    {
        // Act: the quote on line 2 opens no value, so reading stops at it; the text from 'b' to the full stop is skipped.
        var model = Read("a: \"one\".\nb: \"two.\nc: \"three\".\n", Recover);

        // Assert.
        Assert.Equal(["a", "c"], model.Elements.Select(e => e.Id));
        Assert.Equal(new SourceLocation("body.t", 2, 4, 5), Unparsed(model).Location);
    }

    [Fact]
    public void TextNoTerminalMatches_IsPassedOverToTheNextFullStop()
    {
        // Act: no terminal matches '?', so the skip passes both, then lexes the value and the full stop.
        var model = Read("a: \"one\".\nb: ?? \"two\".\nc: \"three\".\n", Recover);

        // Assert.
        Assert.Equal(["a", "c"], model.Elements.Select(e => e.Id));
        Assert.Equal(new SourceLocation("body.t", 2, 4, 9), Unparsed(model).Location);
    }

    [Fact]
    public void AStatementFollowedByTextNoTerminalMatches_IsStillRead()
    {
        // Act: after the full stop of 'a' the lexer finds no token, where the parser has shifted the stop and accepts 'a'.
        var model = Read("a: \"one\". ?? b: \"two\".\nc: \"three\".\n", Recover);

        // Assert.
        Assert.Equal(["a", "c"], model.Elements.Select(e => e.Id));
        Assert.Equal(new SourceLocation("body.t", 1, 11, 12), Unparsed(model).Location);
    }

    [Fact]
    public void TheTokensReadBeforeTheStop_AreNoTokensOrCommentsOfTheReading()
    {
        // Arrange: 'b', its separator and the comment after it are cut before reading stops at the line ending.
        const string body = "a: \"one\".\nb: # note\n\"two\".\n";

        // Act.
        var reading = new GrammarParser(Binding(Recover).Body.Grammar!.Built!, new BodyText(Encoding.UTF8.GetBytes(body))).Parse();

        // Assert.
        var skipped = Assert.Single(reading.Skipped).Text;
        Assert.Equal(new Span(10, 26), skipped);
        Assert.Empty(reading.Comments);
        Assert.DoesNotContain(reading.Tokens, token => token != skipped && token.Start >= skipped.Start && token.Start < skipped.End);
    }

    [Fact]
    public void SkippedTextIsLexedByTheLongestMatch()
    {
        // Arrange: a statement may also end in an ellipsis, which is no synchronising token.
        var grammar = Statements.Replace("VALUE \".\"", "VALUE (\".\" | \"...\")", StringComparison.Ordinal);

        // Act: the ellipsis after 'two' is lexed whole (ATD §3.6), so the skip runs on to the full stop that ends 'c'.
        var model = Read("a: \"one\".\nb: ?? \"two\"...\nc: \"three\".\n", Recover, grammar);

        // Assert.
        Assert.Equal(["a"], model.Elements.Select(e => e.Id));
        Assert.Equal(new SourceLocation("body.t", 2, 4, 23), Unparsed(model).Location);
    }

    [Fact]
    public void ABodyThatEndsInsideAStatement_IsSkippedToItsEnd()
    {
        // Act: the last statement has no full stop, so reading stops at the end of the body.
        var model = Read("a: \"one\".\nb: \"two\"", Recover);

        // Assert.
        Assert.Equal(["a"], model.Elements.Select(e => e.Id));
        Assert.Equal(new SourceLocation("body.t", 2, 9, 0), Unparsed(model).Location);
    }

    [Fact]
    public void WithNoFullStopAfterTheStop_TheRestOfTheBodyIsSkipped()
    {
        // Act: the statements after the unclosed string end in no full stop either.
        var model = Read("a: \"one\".\nb: \"two\nc: \"three\"\n", Recover);

        // Assert.
        Assert.Equal(["a"], model.Elements.Select(e => e.Id));
        Assert.Equal(new SourceLocation("body.t", 2, 4, 16), Unparsed(model).Location);
    }

    [Fact]
    public void TwoBrokenStatements_AreEachSkippedAndReported_AndTheRestRead()
    {
        // Act.
        var model = Read("a: \"one\".\nb: \"two.\nc: \"three\".\nd: ?.\ne: \"five\".\n", Recover);

        // Assert.
        Assert.Equal(["a", "c", "e"], model.Elements.Select(e => e.Id));
        Assert.Equal([2, 4], model.Findings.Where(f => f.Code == FindingCodes.UnparsedText).Select(f => f.Location!.Line));
        Assert.All(model.Findings, f => Assert.Equal(FindingSeverity.Warning, f.Severity));
    }

    [Fact]
    public void SkippedTextIsKeptAsItIs_AndBindsNothing()
    {
        // Arrange.
        const string body = "a: \"one\".\nb: \"two.\nc: \"three\".\n";

        // Act.
        var reading = Rules.BodyReading.Read(Encoding.UTF8.GetBytes(body), Binding(Recover), new FblOptions { FileName = "body.t" });

        // Assert: every byte is accounted for, the skipped text as one leaf no element covers.
        Assert.Empty(reading.Family.Unaccounted());
        Assert.DoesNotContain(reading.Elements, e => e.Entry.Own is { Start: < 19, End: > 10 });
    }

    [Fact]
    public void AnEditOfABodyReadInPart_KeepsTheSkippedText_AndStillReportsIt()
    {
        // Arrange.
        var body = OpenBody.Open("a: \"one\".\nb: \"two.\nc: \"three\".\n"u8.ToArray(), Binding(Recover), new FblOptions { FileName = "body.t" });

        // Act: the statement before the skipped text is removed.
        var result = body.Change(new ModelChange.Remove("a"));

        // Assert: the skipped text is kept byte for byte and still reported, where it now stands (Requirement 5.5).
        Assert.IsType<PlanResult.Planned>(result);
        Assert.Equal("b: \"two.\nc: \"three\".\n", Encoding.UTF8.GetString(body.Bytes));
        Assert.Equal(["c"], body.Model.Elements.Select(e => e.Id));
        Assert.Equal(new SourceLocation("body.t", 1, 4, 5), Unparsed(body.Model).Location);
    }

    [Theory]
    [InlineData("w3c-turtle-grammar.fbl", "turtle", "fixtures/w3c-turtle-grammar/people.ttl")]
    [InlineData("fixtures/grammar-split-slot/labels.fbl", "labels", "fixtures/grammar-split-slot/labels.txt")]
    public void AVendoredBodyThatParses_GivesTheSameTreeWithAndWithoutRecover(string document, string name, string body)
    {
        // Arrange.
        var path = Path.Combine(Repository.Conformance, document);
        var json = JsonNode.Parse(File.ReadAllText(path))!;
        var bytes = File.ReadAllBytes(Path.Combine(Repository.Conformance, body));
        var withRecover = Built(json, name, Path.GetDirectoryName(path)!);
        json["bindings"]![name]!["body"]!.AsObject().Remove("recover");
        var withoutRecover = Built(json, name, Path.GetDirectoryName(path)!);

        // Act.
        var inPart = new GrammarParser(withRecover, new BodyText(bytes)).Parse();
        var whole = new GrammarParser(withoutRecover, new BodyText(bytes)).Parse();

        // Assert.
        Assert.NotNull(withRecover.Recover);
        Assert.Null(withoutRecover.Recover);
        Assert.Empty(inPart.Skipped);
        Assert.Equal(Shape(whole.Root), Shape(inPart.Root));
        Assert.Equal(whole.Tokens, inPart.Tokens);
        Assert.Equal(whole.Comments, inPart.Comments);
    }

    /// <summary>
    /// Entries of a key and any number of parts, a left-recursive repetition the tree builder extends in place: the token
    /// that ends a unit makes the parser reduce before it refuses the token, and the unit is accepted from the state before.
    /// </summary>
    private const string Parts = "start: statement*\n?statement: entry\n          | _NL\nentry: KEY _part*\n_part: plus\n     | mark\n     | block\nplus: \"+\" KEY\nmark: \"!\"\nblock: OPEN KEY* CLOSE\nOPEN: \"{\"\nCLOSE: \"}\"\nKEY: /[a-z]+/\n_NL: /\\n/\nWS: /[ \\t]+/\n%ignore WS\n";

    [Theory]
    [InlineData("""{ "unit": "statement", "sync": ["_NL"] }""")]
    [InlineData("""{ "unit": "statement", "sync": ["_NL"], "nested": { "open": "OPEN", "close": "CLOSE" } }""")]
    public void AUnitTheParserReducedInBeforeRefusingItsNextToken_HoldsEachPartOnce(string recover)
    {
        // Arrange: a mark and a plus after it, and a plus and a mark after it.
        const string body = "k !+a\nm +b!\n";
        var json = JsonNode.Parse($$"""{ "fbl": "0.9", "bindings": { "b": { "claims": { "extensions": [".t"] }, "body": { "kind": "file", "family": "grammar", "grammar": {{JsonSerializer.Serialize(Parts)}}, "recover": {{recover}} }, "reader": "declared", "elements": [ { "name": "entry", "type": "Entry", "at": "/entry" } ], "relations": [] } } }""")!;
        var withRecover = Built(json, "b", Repository.Conformance);
        json["bindings"]!["b"]!["body"]!.AsObject().Remove("recover");
        var withoutRecover = Built(json, "b", Repository.Conformance);

        // Act.
        var inPart = new GrammarParser(withRecover, new BodyText(Encoding.UTF8.GetBytes(body))).Parse();
        var whole = new GrammarParser(withoutRecover, new BodyText(Encoding.UTF8.GetBytes(body))).Parse();

        // Assert: the same tree as the body read whole, the part after the first one not read a second time.
        Assert.Empty(inPart.Skipped);
        Assert.Equal(Shape(whole.Root), Shape(inPart.Root));
    }

    [Fact]
    public void AStopInsideABlock_ResumesFromItsBoundary_WithEveryLineBeforeItOnce()
    {
        // Arrange: a block of a line per key, its fourth line two keys it does not take, after the parser reduced the third into the block.
        const string grammar = "start: statement*\n?statement: entry\n          | _NL\nentry: KEY [block]\nblock: OPEN _line* CLOSE\n_line: KEY _NL\n     | _NL\nOPEN: \"{\"\nCLOSE: \"}\"\nKEY: /[a-z]+/\n_NL: /\\n/\nWS: /[ \\t]+/\n%ignore WS\n";
        const string body = "k {\na\nb\nc\nd d\ne\n}\n";
        var json = JsonNode.Parse($$"""{ "fbl": "0.9", "bindings": { "b": { "claims": { "extensions": [".t"] }, "body": { "kind": "file", "family": "grammar", "grammar": {{JsonSerializer.Serialize(grammar)}}, "recover": { "unit": "statement", "sync": ["_NL"], "nested": { "open": "OPEN", "close": "CLOSE" } } }, "reader": "declared", "elements": [ { "name": "entry", "type": "Entry", "at": "/entry" } ], "relations": [] } } }""")!;

        // Act.
        var reading = new GrammarParser(Built(json, "b", Repository.Conformance), new BodyText(Encoding.UTF8.GetBytes(body))).Parse();

        // Assert: the fourth line skipped, and the block holding each other line once.
        Assert.Single(reading.Skipped);
        var block = reading.Nodes.Single(node => node.Name == "block");
        Assert.Equal("a b c e", string.Join(" ", block.Children.Where(child => child.Name == "KEY").Select(child => Encoding.UTF8.GetString(Encoding.UTF8.GetBytes(body), child.Own.Start, child.Own.End - child.Own.Start))));
    }

    private static Finding Unparsed(FblModel model) => Assert.Single(model.Findings, f => f.Code == FindingCodes.UnparsedText);

    private static FblModel Read(string body, string? recover, string grammar = Statements) =>
        OpenBody.Open(Encoding.UTF8.GetBytes(body), Binding(recover, grammar), new FblOptions { FileName = "body.t" }).Model;

    private static FblBinding Binding(string? recover, string grammar = Statements)
    {
        var recovering = recover is null ? "" : $$""", "recover": {{recover}}""";
        var json = $$"""{ "fbl": "0.9", "bindings": { "b": { "claims": { "extensions": [".t"] }, "body": { "kind": "file", "family": "grammar", "grammar": {{JsonSerializer.Serialize(grammar)}}{{recovering}} }, "reader": "declared", "elements": [ { "name": "entry", "type": "Entry", "at": "/entry", "id": { "from": { "node": "KEY" } }, "remove": {} } ], "relations": [] } } }""";
        var problems = FblDocumentLoader.Load(Encoding.UTF8.GetBytes(json), null, out var document);
        Assert.True(document is not null, string.Join("\n", problems));
        return document.Bindings["b"];
    }

    private static GrammarBuild Built(JsonNode json, string name, string folder)
    {
        var problems = FblDocumentLoader.Load(Encoding.UTF8.GetBytes(json.ToJsonString()), folder, out var document);
        Assert.True(document is not null, string.Join("\n", problems));
        return document.Bindings[name].Body.Grammar!.Built!;
    }

    /// <summary>The tree as text: a node as its name, span and parts, a token as its terminal and span, an empty part as '-'.</summary>
    private static string Shape(GrammarNode node) =>
        node.Empty ? "-" : node.IsToken ? $"{node.Name}@{node.Own.Start}" : $"{node.Name}@{node.Own.Start}-{node.Own.End}({string.Join(" ", node.Parts.Select(Shape))})";
}
