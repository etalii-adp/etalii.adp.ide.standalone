using System.Text;
using System.Text.Json;
using EtAlii.Adp.Specification.Fbl.Documents;
using EtAlii.Adp.Specification.Fbl.Grammar;
using EtAlii.Adp.Specification.Json;
using Xunit;

namespace EtAlii.Adp.Specification.Fbl.Tests.Grammar;

/// <summary>
/// Loading the grammar of a binding of FBL 0.9's <c>grammar</c> family (FBL §4.9, §14.1 step 6, §16; etalii.adp spec 072,
/// Requirements 2 and 3): the grammar is ATD grammar notation, imported only from beside the FBL document, bounded, and
/// built by <c>EtAlii.Lark</c> as a strict LALR(1) automaton. Every refusal is shown on a binding written for the test that
/// is seen to load first, and to be refused once its one defect is planted (Requirement 2.8).
/// </summary>
public class GrammarLoadingTests
{
    /// <summary>A grammar that builds: lines of named words, one per line, read unit by unit.</summary>
    private const string Words = "start: unit*\n?unit: word\n     | _NL\nword: NAME [VALUE]\nNAME: /[a-z]+/\nVALUE: /\"[^\"\\n]*\"/\n_NL: /(\\r?\\n[ \\t]*)+/\nWHITESPACE: /[ \\t]+/\n%ignore WHITESPACE\n";

    [Fact]
    public void TheVendoredTurtleGrammar_Builds()
    {
        // Arrange.
        var body = new GrammarBody { File = "w3c-turtle.lark", Start = "turtle_doc", Recover = new GrammarRecover("statement", ["."]) };
        var problems = new List<LoadDiagnostic>();

        // Act.
        var built = GrammarBuild.Build(body, Repository.Conformance, "/body", problems);

        // Assert.
        Assert.Empty(problems);
        Assert.NotNull(built);
        Assert.Equal("DOT", built.Terminal("."));
        Assert.Contains("triples_statement", built.Names);
        Assert.Contains("COMMENT", built.Ignored);
    }

    [Fact]
    public void ABindingWithAGrammar_Loads()
    {
        // Act.
        var problems = Load(Binding(Words));

        // Assert.
        Assert.DoesNotContain(problems, problem => problem.Severity == LoadSeverity.Error);
    }

    [Theory]
    [InlineData("word: NAME [VALUE]", "!word: NAME [VALUE]", "keep-all-tokens rule")]
    [InlineData("word: NAME [VALUE]", "word.2: NAME [VALUE]", "rule priority")]
    [InlineData("NAME: /[a-z]+/", "NAME: /[a-z]+/i", "the flag 'i'")]
    [InlineData("NAME: /[a-z]+/", "NAME: /^[a-z]+/", "the anchor '^'")]
    [InlineData("NAME: /[a-z]+/", "NAME: /[a-z]+?/", "lazy or possessive quantifier")]
    [InlineData("NAME: /[a-z]+/", "NAME: /[a-z].+/", "'.' outside a character class")]
    [InlineData("NAME: /[a-z]+/", "NAME: /(?<n>[a-z]+)/", "the group '(?<'")]
    [InlineData("NAME: /[a-z]+/", "NAME: /\\p{L}[a-z]*/", "the escape '\\p'")]
    [InlineData("NAME: /[a-z]+/", "NAME: \"a\"..\"z\"", "the range '..'")]
    [InlineData("%ignore WHITESPACE", "%ignore WHITESPACE\n%declare OTHER", "'%declare' is not admitted")]
    [InlineData("%ignore WHITESPACE", "%ignore WHITESPACE\n%import common.WS", "an import from anywhere but a grammar file beside")]
    public void AGrammarOutsideTheNotation_IsRefused(string kept, string planted, string named)
    {
        // Arrange: the binding loads before the defect is planted.
        Assert.DoesNotContain(Load(Binding(Words)), problem => problem.Severity == LoadSeverity.Error);

        // Act.
        var problems = Load(Binding(Words.Replace(kept, planted, StringComparison.Ordinal)));

        // Assert.
        var problem = Assert.Single(problems, p => p.Severity == LoadSeverity.Error);
        Assert.Equal("/bindings/b/body/grammar", problem.Pointer);
        Assert.Contains(named, problem.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AShiftReduceConflict_IsRefusedNamingItsTerminalAndRule()
    {
        // Arrange.
        var conflicting = Words.Replace("?unit: word", "?unit: word | sum\nsum: sum \"+\" sum | NAME", StringComparison.Ordinal);
        Assert.DoesNotContain(Load(Binding(Words)), problem => problem.Severity == LoadSeverity.Error);

        // Act.
        var problems = Load(Binding(conflicting));

        // Assert.
        var problem = Assert.Single(problems, p => p.Severity == LoadSeverity.Error);
        Assert.Equal("/bindings/b/body/grammar", problem.Pointer);
        Assert.Contains("Shift/Reduce conflict for terminal PLUS", problem.Message, StringComparison.Ordinal);
        Assert.Contains("<sum : sum PLUS sum>", problem.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AReduceReduceConflict_IsRefusedNamingItsRules()
    {
        // Arrange.
        var conflicting = Words.Replace("word: NAME [VALUE]", "word: one | other\none: NAME\nother: NAME", StringComparison.Ordinal);

        // Act.
        var problems = Load(Binding(conflicting));

        // Assert.
        var problem = Assert.Single(problems, p => p.Severity == LoadSeverity.Error);
        Assert.Contains("Reduce/Reduce collision", problem.Message, StringComparison.Ordinal);
        Assert.Contains("<one : NAME>", problem.Message, StringComparison.Ordinal);
        Assert.Contains("<other : NAME>", problem.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TwoTerminalsAStateAcceptsMatchingTheSameText_AreRefused()
    {
        // Arrange.
        var colliding = Words.Replace("word: NAME [VALUE]", "word: (NAME | LETTERS) [VALUE]\nLETTERS: /[a-c]+/", StringComparison.Ordinal);

        // Act.
        var problems = Load(Binding(colliding));

        // Assert.
        var problem = Assert.Single(problems, p => p.Severity == LoadSeverity.Error);
        Assert.Contains("Collision between Terminals", problem.Message, StringComparison.Ordinal);
        Assert.Contains("LETTERS", problem.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AGrammarFileBesideTheDocument_IsReadAndAMissingOneRefused()
    {
        // Arrange.
        var folder = Directory.CreateTempSubdirectory("fbl-grammar-").FullName;
        File.WriteAllText(Path.Combine(folder, "words.lark"), Words);
        var json = BindingWithFile("words.lark");

        // Act.
        var found = FblDocumentLoader.Load(Encoding.UTF8.GetBytes(json), folder, out _);
        File.Delete(Path.Combine(folder, "words.lark"));
        var missing = FblDocumentLoader.Load(Encoding.UTF8.GetBytes(json), folder, out var document);

        // Assert.
        Assert.DoesNotContain(found, problem => problem.Severity == LoadSeverity.Error);
        var problem = Assert.Single(missing, p => p.Severity == LoadSeverity.Error);
        Assert.Equal("/bindings/b/body/grammar/file", problem.Pointer);
        Assert.Null(document);
    }

    [Fact]
    public void AnImportFromAFileBesideTheDocument_IsReadAndOneFromElsewhereRefused()
    {
        // Arrange.
        var folder = Directory.CreateTempSubdirectory("fbl-grammar-").FullName;
        var terms = Path.Combine(folder, "terms.lark");
        File.WriteAllText(terms, "NAME: /[a-z]+/\n");
        var importing = Words.Replace("NAME: /[a-z]+/\n", "%import .terms (NAME)\n", StringComparison.Ordinal);
        var json = Encoding.UTF8.GetBytes(Binding(importing));

        // Act.
        var beside = FblDocumentLoader.Load(json, folder, out _);
        File.Move(terms, Path.Combine(Directory.CreateDirectory(Path.Combine(folder, "sub")).FullName, "terms.lark"));
        var elsewhere = FblDocumentLoader.Load(json, folder, out _);

        // Assert.
        Assert.DoesNotContain(beside, problem => problem.Severity == LoadSeverity.Error);
        var problem = Assert.Single(elsewhere, p => p.Severity == LoadSeverity.Error);
        Assert.Contains("The grammar imports 'terms', which is no grammar file beside the FBL document", problem.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AGrammarBeyondTheTextBound_IsRefusedSayingSo()
    {
        // Arrange: a comment long enough to pass the bound.
        var huge = Words + "// " + new string('x', GrammarBuild.MaxTextBytes) + "\n";

        // Act.
        var problems = Load(Binding(huge));

        // Assert.
        var problem = Assert.Single(problems, p => p.Severity == LoadSeverity.Error);
        Assert.Contains($"more than the {GrammarBuild.MaxTextBytes} a binding may carry", problem.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AGrammarBeyondTheRuleBound_IsRefusedSayingSo()
    {
        // Arrange: thirteen optional keywords expand one rule into 8,192 alternatives, which build in under a second.
        var optional = string.Concat(Enumerable.Range(0, 13).Select(i => $" \"k{(char)('a' + i)}\"?"));
        var expanding = Words.Replace("word: NAME [VALUE]", "word: NAME [VALUE]" + optional, StringComparison.Ordinal);

        // Act.
        var problems = Load(Binding(expanding));

        // Assert.
        var problem = Assert.Single(problems, p => p.Severity == LoadSeverity.Error);
        Assert.Contains($"more than the {GrammarBuild.MaxRules} a binding's grammar may have", problem.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AGrammarBeyondTheStateBound_IsRefusedSayingSo()
    {
        // Arrange: 3,000 alternatives of twelve symbols each stay under the rule bound and build 36,000 states in about two seconds.
        var alternatives = string.Join(" | ", Enumerable.Range(0, 3000).Select(i => $"\"k{i}\"" + string.Concat(Enumerable.Repeat(" NAME", 11))));
        var lengthy = Words.Replace("?unit: word", "?unit: word | long", StringComparison.Ordinal) + "long: " + alternatives + "\n";

        // Act.
        var problems = Load(Binding(lengthy));

        // Assert.
        var problem = Assert.Single(problems, p => p.Severity == LoadSeverity.Error);
        Assert.Equal("/bindings/b/body/grammar", problem.Pointer);
        Assert.Contains($"more than the {GrammarBuild.MaxStates} a binding's grammar may have", problem.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("\"unit\"", "\"item\"", "/bindings/b/body/recover/unit", "no rule of the grammar")]
    [InlineData("\"unit\"", "\"word\"", "/bindings/b/body/recover/unit", "is not 'word*'")]
    [InlineData("[\"_NL\"]", "[\"NOTHING\"]", "/bindings/b/body/recover/sync/0", "no terminal of the grammar")]
    public void ARecoverThatDoesNotFitTheGrammar_IsRefused(string kept, string planted, string pointer, string named)
    {
        // Arrange.
        var binding = Binding(Words);
        Assert.DoesNotContain(Load(binding), problem => problem.Severity == LoadSeverity.Error);

        // Act.
        var problems = Load(binding.Replace("\"unit\": " + kept, "\"unit\": " + planted, StringComparison.Ordinal).Replace("\"sync\": " + kept, "\"sync\": " + planted, StringComparison.Ordinal));

        // Assert.
        var problem = Assert.Single(problems, p => p.Severity == LoadSeverity.Error);
        Assert.Equal(pointer, problem.Pointer);
        Assert.Contains(named, problem.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ASyncEntryMayBeTheTextOfAnAnonymousStringTerminal()
    {
        // Arrange.
        var dotted = Words.Replace("word: NAME [VALUE]", "word: NAME [VALUE] \".\"", StringComparison.Ordinal);

        // Act.
        var problems = Load(Binding(dotted).Replace("[\"_NL\"]", "[\".\"]", StringComparison.Ordinal));

        // Assert.
        Assert.DoesNotContain(problems, problem => problem.Severity == LoadSeverity.Error);
    }

    /// <summary>A rule of the family that reads each word's name and value through <c>{node}</c> slots, the value without its quotes.</summary>
    private const string WordRule = """{ "name": "word", "type": "Word", "at": "/word", "id": { "from": { "node": "NAME" } }, "attributes": { "value": { "node": "VALUE", "match": "\"(?<value>[^\"]*)\"" } } }""";

    [Fact]
    public void ARuleThatReadsThroughNodeSlots_Loads()
    {
        // Act.
        var problems = Load(Binding(Words, WordRule));

        // Assert.
        Assert.Empty(problems);
    }

    [Theory]
    [InlineData("\"at\": \"/word\"", "\"at\": \"/**/word\"")]
    [InlineData("\"at\": \"/word\"", "\"at\": \"/*\"")]
    [InlineData("\"at\": \"/word\"", "\"at\": \"/word[NAME='a']\"")]
    [InlineData("\"at\": \"/word\"", "\"at\": \"/\"")]
    [InlineData("{ \"node\": \"NAME\" }", "{ \"node\": \".\" }")]
    [InlineData("{ \"node\": \"NAME\" }", "{ \"node\": \"**/NAME\" }")]
    public void ASelectorOfTheFamily_Loads(string kept, string written)
    {
        // Act.
        var problems = Load(Binding(Words, WordRule.Replace(kept, written, StringComparison.Ordinal)));

        // Assert.
        Assert.Empty(problems);
    }

    [Theory]
    [InlineData("\"at\": \"/word\"", "\"at\": \"/wurd\"", "/bindings/b/elements/0/at", "names 'wurd', which is no rule, alias or terminal")]
    [InlineData("\"at\": \"/word\"", "\"at\": \"/word[NAMES='a']\"", "/bindings/b/elements/0/at", "names 'NAMES', which is no rule, alias or terminal")]
    [InlineData("\"at\": \"/word\"", "\"at\": \"/word/{key}\"", "/bindings/b/elements/0/at", "is not a selector of the grammar family")]
    [InlineData("\"at\": \"/word\"", "\"at\": \".\"", "/bindings/b/elements/0/at", "is not a selector of the grammar family")]
    [InlineData("{ \"node\": \"NAME\" }", "{ \"node\": \"NAMEX\" }", "/bindings/b/elements/0/id/from/node", "names 'NAMEX', which is no rule, alias or terminal")]
    [InlineData("{ \"node\": \"NAME\" }", "{ \"node\": \"./NAME\" }", "/bindings/b/elements/0/id/from/node", "is not a selector of the grammar family")]
    [InlineData("\"at\": \"/word\"", "\"line\": \"^(?<w>.*)$\"", "/bindings/b/elements/0", "A rule of the grammar family has 'at' and no 'line'")]
    [InlineData("\"match\": \"\\\"(?<value>[^\\\"]*)\\\"\"", "\"match\": \"\\\"(?<text>[^\\\"]*)\\\"\"", "/bindings/b/elements/0/attributes/value/match", "has the named group 'value'")]
    [InlineData("{ \"node\": \"VALUE\",", "{ \"key\": \"VALUE\",", "/bindings/b/elements/0/attributes/value", "A {key} slot is not admitted in the grammar family")]
    [InlineData("{ \"node\": \"VALUE\",", "{ \"attribute\": \"VALUE\",", "/bindings/b/elements/0/attributes/value", "A {attribute} slot is not admitted in the grammar family")]
    [InlineData("{ \"node\": \"VALUE\",", "{ \"text\": true,", "/bindings/b/elements/0/attributes/value", "A {text} slot is not admitted in the grammar family")]
    [InlineData("{ \"node\": \"VALUE\",", "{ \"group\": \"VALUE\",", "/bindings/b/elements/0/attributes/value", "A {group} slot is not admitted in the grammar family")]
    [InlineData("{ \"node\": \"VALUE\",", "{ \"continuation\": true,", "/bindings/b/elements/0/attributes/value", "A {continuation} slot is not admitted in the grammar family")]
    [InlineData("{ \"node\": \"VALUE\",", "{ \"capture\": \"VALUE\",", "/bindings/b/elements/0/attributes/value", "A {capture} slot is not admitted in the grammar family")]
    public void ARuleOutsideTheFamily_IsRefused(string kept, string planted, string pointer, string named)
    {
        // Arrange.
        Assert.Empty(Load(Binding(Words, WordRule)));

        // Act.
        var problems = Load(Binding(Words, WordRule.Replace(kept, planted, StringComparison.Ordinal)));

        // Assert.
        var problem = Assert.Single(problems, p => p.Severity == LoadSeverity.Error);
        Assert.Equal(pointer, problem.Pointer);
        Assert.Contains(named, problem.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ABodyOfTheFamilyWithoutAGrammar_IsRefused()
    {
        // Arrange.
        var binding = Binding(Words);
        Assert.Empty(Load(binding));
        var grammar = $", \"grammar\": {JsonSerializer.Serialize(Words)}, \"recover\": {{ \"unit\": \"unit\", \"sync\": [\"_NL\"] }}";
        Assert.Contains(grammar, binding, StringComparison.Ordinal);

        // Act.
        var problems = Load(binding.Replace(grammar, "", StringComparison.Ordinal));

        // Assert.
        var problem = Assert.Single(problems, p => p.Severity == LoadSeverity.Error);
        Assert.Equal("/bindings/b/body", problem.Pointer);
        Assert.Equal("A body of the grammar family carries its grammar (FBL 0.9 4.9).", problem.Message);
    }

    [Fact]
    public void AGrammarOnABodyOfAnotherFamily_IsRefused()
    {
        // Arrange.
        var yaml = """{ "fbl": "0.9", "bindings": { "b": { "claims": { "extensions": [".y"] }, "body": { "kind": "file", "family": "yaml" }, "reader": "declared", "elements": [ { "name": "item", "type": "Item", "at": "/items/*", "attributes": { "name": { "key": "name" } } } ] } } }""";
        Assert.Empty(Load(yaml));

        // Act.
        var problems = Load(yaml.Replace("\"family\": \"yaml\"", "\"family\": \"yaml\", \"grammar\": \"start: NAME\\nNAME: /[a-z]+/\\n\"", StringComparison.Ordinal));

        // Assert.
        var problem = Assert.Single(problems, p => p.Severity == LoadSeverity.Error);
        Assert.Equal("/bindings/b/body/grammar", problem.Pointer);
        Assert.Equal("A grammar is given only on a body of the grammar family (FBL 0.9 4.9).", problem.Message);
    }

    [Fact]
    public void ANodeSlotInAnotherFamily_IsRefused()
    {
        // Arrange.
        var yaml = """{ "fbl": "0.9", "bindings": { "b": { "claims": { "extensions": [".y"] }, "body": { "kind": "file", "family": "yaml" }, "reader": "declared", "elements": [ { "name": "item", "type": "Item", "at": "/items/*", "attributes": { "name": { "key": "name" } } } ] } } }""";
        Assert.Empty(Load(yaml));

        // Act.
        var problems = Load(yaml.Replace("{ \"key\": \"name\" }", "{ \"node\": \"name\" }", StringComparison.Ordinal));

        // Assert.
        var problem = Assert.Single(problems, p => p.Severity == LoadSeverity.Error);
        Assert.Equal("/bindings/b/elements/0/attributes/name/node", problem.Pointer);
        Assert.Equal("A {node} slot is admitted only in the grammar family (FBL 0.9 4.9).", problem.Message);
    }

    /// <summary>
    /// Requirement 8.1: FBL 0.11's <c>recover.nested</c> is a construct of the family newer than the 0.9 the library reads,
    /// so the binding loads with FBL §2.7's warning, which says it is not read, and with nothing else.
    /// </summary>
    [Fact]
    public void ABindingWithANewerConstructOfTheFamily_LoadsWithTheWarningAndTheConstructIgnored()
    {
        // Arrange.
        var newer = Binding(Words, WordRule)
            .Replace("\"fbl\": \"0.9\"", "\"fbl\": \"0.11\"", StringComparison.Ordinal)
            .Replace("\"sync\": [\"_NL\"] }", "\"sync\": [\"_NL\"], \"nested\": { \"open\": \"{\", \"close\": \"}\" } }", StringComparison.Ordinal);

        // Act.
        var problems = FblDocumentLoader.Load(Encoding.UTF8.GetBytes(newer), null, out var document);

        // Assert.
        var warning = Assert.Single(problems);
        Assert.Equal(new LoadDiagnostic("/fbl", LoadSeverity.Warning, "This runtime knows FBL up to 0.9; what FBL 0.11 adds is not read (FBL §2.7)."), warning);
        var body = Assert.IsType<FblDocument>(document).Bindings["b"].Body;
        Assert.Equal("unit", body.Grammar!.Recover!.Unit);
        Assert.Equal(["_NL"], body.Grammar.Recover.Sync);
    }

    /// <summary>
    /// Requirement 8.2 (FBL §15.1): a binding refused under Requirement 2 yields no document, and its problems are the
    /// reason a host gives when it opens the binding's documents read-only, each with its pointer and words.
    /// </summary>
    [Fact]
    public void ARefusedBindingOfTheFamily_YieldsNoDocumentAndItsReason()
    {
        // Arrange.
        var refused = Binding(Words, WordRule.Replace("\"at\": \"/word\"", "\"at\": \"/wurd\"", StringComparison.Ordinal));

        // Act.
        var problems = FblDocumentLoader.Load(Encoding.UTF8.GetBytes(refused), null, out var document);

        // Assert.
        Assert.Null(document);
        Assert.Equal(
            "error at /bindings/b/elements/0/at: The selector '/wurd' names 'wurd', which is no rule, alias or terminal of the grammar (FBL 0.9 4.9).",
            Assert.Single(problems).ToString());
    }

    private static IReadOnlyList<LoadDiagnostic> Load(string json) => FblDocumentLoader.Load(Encoding.UTF8.GetBytes(json), null, out _);

    /// <summary>A binding of the family over <paramref name="grammar"/>, whose one rule reads a computed value, so that only its grammar can refuse it.</summary>
    private static string Binding(string grammar, string rule = DefaultRule) => Document(JsonSerializer.Serialize(grammar), rule);

    private static string BindingWithFile(string file) => Document($$"""{ "file": "{{file}}" }""", DefaultRule);

    private const string DefaultRule = """{ "name": "word", "type": "Word", "at": "/word", "attributes": { "kind": { "value": "'word'" } } }""";

    private static string Document(string grammar, string rule) => $$"""
        {
          "fbl": "0.9",
          "bindings": {
            "b": {
              "title": "Words",
              "claims": { "extensions": [".words"] },
              "body": { "kind": "file", "family": "grammar", "grammar": {{grammar}}, "recover": { "unit": "unit", "sync": ["_NL"] } },
              "reader": "declared",
              "elements": [ {{rule}} ]
            }
          }
        }
        """;
}
