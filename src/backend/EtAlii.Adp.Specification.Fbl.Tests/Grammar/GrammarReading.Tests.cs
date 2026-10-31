using System.Text;
using System.Text.Json;
using EtAlii.Adp.Specification.Fbl.Documents;
using EtAlii.Adp.Specification.Fbl.Files.Grammar;
using EtAlii.Adp.Specification.Fbl.Planning;
using EtAlii.Adp.Specification.Fbl.Rules;
using EtAlii.Adp.Specification.Fbl.Text;
using EtAlii.Adp.Specification.Json;
using Xunit;

namespace EtAlii.Adp.Specification.Fbl.Tests.Grammar;

/// <summary>
/// Reading a body of FBL 0.9's grammar family (FBL §4.9, ATD §4.5; etalii.adp spec 072, Requirements 3.2 and 4): the tree
/// a grammar gives, its spans in bytes, the comments, the selectors, CEL, the slots, ids and references, and that what no
/// rule maps is kept.
/// </summary>
public class GrammarReadingTests
{
    /// <summary>
    /// Entries of a key, a value and an optional note per line: <c>?value</c> is replaced by its one child, <c>_SEP</c> and
    /// the anonymous brackets and commas are left out, <c>_words</c> is replaced by its children, a parenthesised word is
    /// the alias <c>paren</c>, and <c>tail</c> matches no token when there is no mark.
    /// </summary>
    private const string Entries = """
        start: (entry | _NL)*
        entry: KEY _SEP value [NOTE] tail _NL
        ?value: atom
              | "[" _words "]"     -> list
              | "(" WORD ")"       -> paren
        atom: WORD
        _words: WORD ("," WORD)*
        tail: [MARK]
        _SEP: ":"
        KEY: /[a-z]+/
        WORD: /[A-Z][a-zé]*/
        NOTE: /#[a-z]+/
        MARK: /![a-z]*/
        COMMENT: /\/\/[^\n]*/
        _NL: /\n/
        WS: /[ \t]+/
        %ignore WS
        %ignore COMMENT

        """;

    private const string EntriesBody = "// lead 😀 é\na: Zoé\nb: [Al, Cy] #x !m // trail\n\nc: (Di)\n";

    [Fact]
    public void TheTreeIsShapedAsAtdSaysForEveryRule()
    {
        // Act.
        var reading = Parse(Entries, EntriesBody);

        // Assert: ATD §4.5's shaping, and the empty optional part kept as an empty part.
        Assert.Equal("start(entry(KEY atom(WORD) - tail(-)) entry(KEY list(WORD WORD) NOTE tail(MARK)) entry(KEY paren(WORD) - tail(-)))", Shape(reading.Root));
    }

    [Fact]
    public void ANodesSpanHoldsTheTokensTheTreeLeavesOut_AndItsTextIsItsBytes()
    {
        // Act.
        var reading = Parse(Entries, EntriesBody);
        var bytes = Encoding.UTF8.GetBytes(EntriesBody);
        var second = (GrammarNode)reading.Root.Children[1];

        // Assert: the separator, the brackets, the comment before the line ending and the line ending are inside the
        // entry's span, as its last token is the line ending; the root's is the body.
        Assert.Equal("b: [Al, Cy] #x !m // trail\n", Text(bytes, second.Own));
        Assert.Equal("[Al, Cy]", Text(bytes, ((GrammarNode)second.Children[1]).Own));
        Assert.Equal(new Span(0, bytes.Length), reading.Root.Own);
    }

    [Fact]
    public void ANodeThatMatchedNoToken_HasNoSpan()
    {
        // Act.
        var reading = Parse(Entries, EntriesBody);
        var first = (GrammarNode)reading.Root.Children[0];
        var second = (GrammarNode)reading.Root.Children[1];

        // Assert.
        Assert.False(first.Parts[3].HasSpan);
        Assert.True(second.Parts[3].HasSpan);
        Assert.True(first.Parts[2].Empty);
    }

    [Fact]
    public void SpansAreBytes_AfterMultiByteCharacters()
    {
        // Act.
        var reading = Parse(Entries, EntriesBody);
        var bytes = Encoding.UTF8.GetBytes(EntriesBody);
        var key = (GrammarNode)reading.Root.Children[1].Children[0];

        // Assert: the comment holds a four-byte and a two-byte character, and the word before the key a two-byte one.
        var expected = Encoding.UTF8.GetByteCount(EntriesBody[..EntriesBody.IndexOf("b:", StringComparison.Ordinal)]);
        Assert.Equal(new Span(expected, expected + 1), key.Own);
        Assert.Equal("Zoé", Text(bytes, reading.Root.Children[0].Children[1].Children[0].Own));
    }

    [Fact]
    public void ACommentIsALeaf_AndIgnoredWhitespaceIsNot()
    {
        // Act.
        var reading = Parse(Entries, EntriesBody);
        var bytes = Encoding.UTF8.GetBytes(EntriesBody);

        // Assert.
        Assert.Equal(["// lead 😀 é", "// trail"], reading.Comments.Select(span => Text(bytes, span)));
        Assert.DoesNotContain(reading.Tokens, span => Text(bytes, span).Trim(' ', '\t').Length == 0);
        Assert.Contains(reading.Tokens, span => Text(bytes, span) == "\n");
    }

    [Fact]
    public void AKeywordIsANameWhereTheStateCannotShiftTheKeyword()
    {
        // Arrange: after 'let' the parser's state shifts a name only, so the contextual lexer cuts 'let' as one (ATD §3.6).
        const string grammar = "start: (decl | _NL)*\ndecl: \"let\" NAME \"=\" NAME _NL\nNAME: /[a-z]+/\n_NL: /\\n/\nWS: /[ \\t]+/\n%ignore WS\n";

        // Act.
        var reading = Parse(grammar, "let let = x\n");
        var bytes = "let let = x\n"u8.ToArray();

        // Assert.
        var decl = (GrammarNode)reading.Root.Children[0];
        Assert.Equal(["let", "x"], decl.Children.Select(child => Text(bytes, child.Own)));
        Assert.All(decl.Children, child => Assert.Equal("NAME", child.Name));
    }

    [Fact]
    public void EverySelectorFormMatchesAsFblSays()
    {
        // Arrange: a name, '*', '**', a test and '.', absolute for a rule and relative for a slot.
        var binding = Binding(Entries, """
            [
              { "name": "entry", "type": "Entry", "at": "/entry", "id": { "from": { "node": "KEY" } },
                "attributes": { "whole": { "node": "." }, "first": { "node": "**/WORD" }, "note": { "node": "NOTE" } } },
              { "name": "word", "type": "Word", "at": "/**/WORD", "id": { "from": { "node": "." } } },
              { "name": "listed", "type": "Listed", "at": "/entry/*", "when": "entry.type == 'list'", "id": { "from": { "node": "." } } },
              { "name": "third", "type": "Third", "at": "/entry[KEY='c']/paren", "id": { "from": { "node": "." } } }
            ]
            """);

        // Act.
        var model = Read(binding, EntriesBody).ToModel();

        // Assert.
        Assert.Equal(["a", "b", "c"], model.Elements.Where(e => e.Type == "Entry").Select(e => e.Id));
        Assert.Equal(["Zoé", "Al", "Cy", "Di"], model.Elements.Where(e => e.Type == "Word").Select(e => e.Id));
        Assert.Equal(["[Al, Cy]"], model.Elements.Where(e => e.Type == "Listed").Select(e => e.Id));
        Assert.Equal(["(Di)"], model.Elements.Where(e => e.Type == "Third").Select(e => e.Id));
        var b = model.Find("b")!;
        Assert.Equal("b: [Al, Cy] #x !m // trail\n", b.Attributes["whole"]);
        Assert.Equal("Al", b.Attributes["first"]);
        Assert.Equal("#x", b.Attributes["note"]);
        Assert.False(model.Find("a")!.Attributes.ContainsKey("note"));
    }

    [Fact]
    public void CelSeesANodeAsItsTypeTextAndChildren_AnEmptyPartNull()
    {
        // Arrange.
        var binding = Binding(Entries, """
            [
              { "name": "noted", "type": "Noted", "at": "/entry", "when": "entry.children[2] != null && entry.children[2].children.size() == 0", "id": { "from": { "node": "KEY" } } },
              { "name": "plain", "type": "Plain", "at": "/entry", "when": "entry.children[2] == null && entry.children[1].type == 'atom' && entry.children[1].text == 'Zoé'", "id": { "from": { "node": "KEY" } } }
            ]
            """);

        // Act.
        var model = Read(binding, EntriesBody).ToModel();

        // Assert.
        Assert.Equal(["b"], model.Elements.Where(e => e.Type == "Noted").Select(e => e.Id));
        Assert.Equal(["a"], model.Elements.Where(e => e.Type == "Plain").Select(e => e.Id));
    }

    [Fact]
    public void AnIdFromANodeSlotAndAReference_AreComparedAsTheTextTheSlotReads()
    {
        // Arrange.
        const string grammar = "start: (node | link | _NL)*\nnode: \"node\" NAME _NL\nlink: NAME \"->\" target _NL\ntarget: NAME\nNAME: /[a-z]+/\n_NL: /\\n/\nWS: /[ \\t]+/\n%ignore WS\n";
        var binding = Binding(grammar,
            """[ { "name": "node", "type": "Node", "at": "/node", "id": { "from": { "node": "NAME" } } } ]""",
            """[ { "name": "link", "type": "Link", "at": "/link", "source": { "node": "NAME", "reference": { "to": ["node"] } }, "target": { "node": "target", "reference": { "to": ["node"] } } } ]""");

        // Act.
        var model = Read(binding, "node a\nnode b\na -> b\n").ToModel();

        // Assert.
        var link = Assert.Single(model.Elements, e => e.IsRelation);
        Assert.Equal(("a", "b"), (link.Source, link.Target));
    }

    [Fact]
    public void ALeadingCommentAndATrailingComment_AreInTheEntrysLineSpan()
    {
        // Arrange.
        var binding = Binding(Entries, """[ { "name": "entry", "type": "Entry", "at": "/entry", "id": { "from": { "node": "KEY" } } } ]""");
        var bytes = Encoding.UTF8.GetBytes(EntriesBody);

        // Act.
        var reading = Read(binding, EntriesBody);

        // Assert: the comment line above 'a' is its leading comment; 'b' keeps its trailing one; the blank line above 'c' stays out.
        Assert.Equal("// lead 😀 é\na: Zoé\n", Text(bytes, reading.Find("a")!.Entry.LineSpan!.Value));
        Assert.Equal("b: [Al, Cy] #x !m // trail\n", Text(bytes, reading.Find("b")!.Entry.LineSpan!.Value));
        Assert.Equal("c: (Di)\n", Text(bytes, reading.Find("c")!.Entry.LineSpan!.Value));
    }

    [Fact]
    public void ATextTheMatchDoesNotMatch_ReadsAsTheDefault_AndIsNotWritable()
    {
        // Arrange: research R8.
        var binding = Binding(Entries, """
            [ { "name": "entry", "type": "Entry", "at": "/entry", "id": { "from": { "node": "KEY" } },
                "attributes": { "word": { "node": "atom/WORD", "match": "Z(?<value>[a-zé]*)", "default": "-" } } } ]
            """);

        // Act.
        var reading = Read(binding, "a: Zoé\nb: Al\n");

        // Assert.
        Assert.Equal("oé", reading.Find("a")!.Attributes["word"]);
        var b = reading.Find("b")!;
        Assert.Equal("-", b.Attributes["word"]);
        Assert.Equal((false, false, "'Al' does not match the slot's match."), (b.Slots["word"].Present, b.Slots["word"].Writable, b.Slots["word"].Reason));
    }

    [Fact]
    public void UnboundTextIsKept_AndAnUnchangedSavePlansNoSplice()
    {
        // Arrange: no rule reads the lists or the parenthesised words.
        var binding = Binding(Entries, """[ { "name": "entry", "type": "Entry", "at": "/entry", "when": "entry.children[1].type == 'atom'", "id": { "from": { "node": "KEY" } } } ]""");
        var reading = Read(binding, EntriesBody);

        // Act.
        var result = EditPlanner.Plan(reading, new ModelChange.Save());

        // Assert.
        Assert.Equal(["a"], reading.Elements.Select(e => e.Id));
        Assert.Empty(reading.Family.Unaccounted());
        Assert.Empty(Assert.IsType<PlanResult.Planned>(result).Edit.Splices);
    }

    private static GrammarReading Parse(string grammar, string body) =>
        new GrammarParser(Binding(grammar, """[ { "name": "x", "type": "X", "at": "/" } ]""").Body.Grammar!.Built!, new BodyText(Encoding.UTF8.GetBytes(body))).Parse();

    private static BodyReading Read(FblBinding binding, string body)
    {
        var reading = BodyReading.Read(Encoding.UTF8.GetBytes(body), binding, new FblOptions { FileName = "body.t" });
        Assert.Null(reading.Unreadable);
        return reading;
    }

    private static FblBinding Binding(string grammar, string elements, string relations = "[]")
    {
        var json = $$"""{ "fbl": "0.9", "bindings": { "b": { "claims": { "extensions": [".t"] }, "body": { "kind": "file", "family": "grammar", "grammar": {{JsonSerializer.Serialize(grammar.Replace("\r\n", "\n", StringComparison.Ordinal))}} }, "reader": "declared", "elements": {{elements}}, "relations": {{relations}} } } }""";
        var problems = FblDocumentLoader.Load(Encoding.UTF8.GetBytes(json), null, out var document);
        Assert.True(document is not null, string.Join("\n", problems));
        return document.Bindings["b"];
    }

    /// <summary>The tree as text: a node as its name and parts, a token as its terminal, an empty part as '-'.</summary>
    private static string Shape(GrammarNode node) =>
        node.Empty ? "-" : node.IsToken ? node.Name! : $"{node.Name}({string.Join(" ", node.Parts.Select(Shape))})";

    private static string Text(byte[] bytes, Span span) => Encoding.UTF8.GetString(bytes, span.Start, span.End - span.Start);
}
