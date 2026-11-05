using EtAlii.Adp.Diagram.Sankey.Tests.Parity;
using EtAlii.Adp.Documents;
using Xunit;

namespace EtAlii.Adp.Diagram.Sankey.Tests;

/// <summary>
/// The body read through the binding (sankey-disl-fbl Requirement 4, task 10): every document of the corpus reads to
/// what the module's hand-written reader read, each of the reader's nine sentences comes from its fixture on its line,
/// and a body that is not YAML opens empty and read-only.
/// </summary>
public class SankeyReaderTests
{
    public static TheoryData<string> Corpus() => SankeyCorpus.Names();

    /// <summary>The nine sentences under <c>sankey.unreadable</c>, each with the fixture it comes from and its 0-based line.</summary>
    public static TheoryData<string, int, string> Sentences() => new()
    {
        { "values-that-will-not-read.skv", 19, "`value: lots` is not a number and was ignored." },
        { "values-that-will-not-read.skv", 10, "`column: 0` is not a whole number from 1; the flows place this node." },
        { "values-that-will-not-read.skv", 3, "`flow-color: sideways` is neither `source` nor `target`; flows take their target's colour." },
        { "values-that-will-not-read.skv", 2, "`thickness: wide` is not a number above zero and was ignored." },
        { "unknown-keys.skv", 6, "`colour` is not a key this module reads on a node; the line is kept." },
        { "malformed-entries.skv", 10, "A node entry is not a mapping and was passed over." },
        { "header-missing.skv", 0, "The document does not begin with `sankey: 1`." },
        { "header-not-a-number.skv", 0, "The version `one` is not a number." },
        { "header-other-version.skv", 0, "This document states version 2; this module reads version 1." },
    };

    [Theory]
    [MemberData(nameof(Corpus))]
    public async Task EveryDocument_ReadsWhatTheHandWrittenReaderRead(string name)
    {
        // Arrange.
        var text = await TextOf(name);

        // Act.
        var expected = HandWrittenSankeyReader.Outcome(LineDocument.Parse(text));
        var actual = SankeyReader.Outcome(text);

        // Assert: the nodes, the flows, the three settings, the version, every sentence on its line, and why it cannot be read.
        Assert.Equal(expected.Model.Settings, actual.Model.Settings);
        Assert.Equal(expected.Model.Version, actual.Model.Version);
        Assert.Equal(expected.Model.Nodes, actual.Model.Nodes);
        Assert.Equal(expected.Model.Flows, actual.Model.Flows);
        Assert.Equal(expected.Problems, actual.Problems);
        Assert.Equal(expected.Unreadable, actual.Unreadable);
    }

    [Theory]
    [MemberData(nameof(Sentences))]
    public async Task EachSentence_ComesFromItsFixture_OnItsLine(string fixture, int line, string sentence)
    {
        // Act.
        var problems = SankeyReader.Parse(await FixtureText(fixture)).Problems;

        // Assert.
        Assert.Contains(new ParseProblem(line, sentence), problems);
    }

    [Theory]
    [InlineData("not-yaml.skv", 2, "The document could not be read as YAML: While parsing a flow sequence, did not find expected ',' or ']'.")]
    [InlineData("duplicate-key.skv", 5, "The document could not be read as YAML: Duplicate key name")]
    public async Task ABodyThatIsNotYaml_OpensEmptyAndReadOnly_WithTheOneSentenceItGivesToday(string fixture, int line, string sentence)
    {
        // Arrange.
        var text = await FixtureText(fixture);

        // Act.
        var body = SankeyBody.Parse(text);
        var outcome = SankeyReader.Outcome(text);

        // Assert: a key written twice is still not YAML here (Q4), though FBL would read the first.
        Assert.True(body.IsReadOnly);
        Assert.False(outcome.IsUsable);
        Assert.Equal(sentence, outcome.Unreadable);
        Assert.Equal(new ParseProblem(line, sentence), Assert.Single(outcome.Problems));
        Assert.Empty(outcome.Model.Nodes);
        Assert.Empty(outcome.Model.Flows);
        Assert.Empty(body.Disl.Diagram.Elements);
    }

    [Fact]
    public async Task ATextValue_IsTheTextItWasWrittenWith_AndAnIdTheIdWritten()
    {
        // Arrange.
        var model = SankeyReader.Parse(await FixtureText("text-that-reads-as-a-number.skv"));

        // Act.
        var diagram = SankeyReader.BodyOf(model)!.Disl.Diagram;

        // Assert: 007, 1, true and 0x1F stay what was written (B1), in the module's model and the definition's (Requirement 4.7).
        Assert.Equal(["1", "007", "true"], model.Nodes.Select(node => node.Id));
        Assert.Equal(["2050", "~", "on"], model.Nodes.Select(node => node.Name));
        Assert.Equal(["1", "007", "true"], diagram.NodesOfType("Node").Select(node => node.Id));
        Assert.Equal(["1->007", "0x1F"], diagram.RelationsOfType("Flow").Select(flow => flow.Id));
    }

    [Fact]
    public async Task AFlowWithAnEndThatNamesNoNode_IsKept_WithThatEndUnset()
    {
        // Arrange.
        var model = SankeyReader.Parse(await FixtureText("rules-broken.skv"));

        // Act.
        var flows = SankeyReader.BodyOf(model)!.Disl.Diagram.RelationsOfType("Flow").Where(flow => flow.TargetId == "ghost").ToList();

        // Assert: the flow is read and kept (B6), and the end it names is what is written, for writtenEnd.
        Assert.Contains(model.Flows, flow => flow.To == "ghost");
        Assert.Null(Assert.Single(flows).Target);
    }

    [Theory]
    [InlineData("")]
    [InlineData("- a\n- b\n")]
    [InlineData("just text")]
    [InlineData("sankey: 1\nnodes: {a: 1}\nflows: 7\n")]
    [InlineData("sankey: 1\nnodes:\n  - [a, b]\n  -\n  - {}\nflows:\n  - from: [a]\n    to: {b: c}\n")]
    [InlineData("sankey: 1\nnodes: [unclosed\n")]
    [InlineData("sankey: &a 1\nformat: *a\nnodes: []\n")]
    public void Reading_NeverThrowsOnContent(string text)
    {
        // Act.
        var exception = Record.Exception(() => _ = SankeyBody.Parse(text).Disl.Diagram.Elements.Count + SankeyReader.Parse(text).Problems.Count);

        // Assert.
        Assert.Null(exception);
    }

    private static async Task<string> TextOf(string name)
    {
        var document = new SankeyTranscript().Corpus().Single(candidate => candidate.Name == name);
        return TranscriptText.Decode(await document.Read(TestContext.Current.CancellationToken));
    }

    private static Task<string> FixtureText(string name) =>
        File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", name), TestContext.Current.CancellationToken);
}
