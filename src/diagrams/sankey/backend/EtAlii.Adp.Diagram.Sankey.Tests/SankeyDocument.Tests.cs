using EtAlii.Adp.Documents;
using Xunit;

namespace EtAlii.Adp.Diagram.Sankey.Tests;

/// <summary>
/// The format: what the reader reads, what it reports instead of throwing, and that every write is
/// a splice planned through the binding which leaves every line it did not mean to touch exactly as it was.
/// </summary>
public sealed class SankeyDocumentTests
{
    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    private static LineDocument Load(string name) => LineDocument.Parse(File.ReadAllText(Fixture(name)));

    private static SankeyBody Body(string name) => SankeyBody.Parse(File.ReadAllText(Fixture(name)));

    private static List<string> Lines(SankeyBody body) => [.. LineDocument.Parse(body.Text).Lines.Select(line => line.Text)];

    public static TheoryData<string> LineEndings => ["crlf-line-endings.skv", "lf-line-endings.skv", "no-trailing-newline.skv"];

    [Theory]
    [MemberData(nameof(LineEndings))]
    public async Task AnUnchangedDocument_ComesBackByteIdentical(string fixture)
    {
        // Arrange.
        var bytes = await File.ReadAllBytesAsync(Fixture(fixture), TestContext.Current.CancellationToken);

        // Act.
        var document = SankeyBody.Parse(await File.ReadAllTextAsync(Fixture(fixture), TestContext.Current.CancellationToken));
        _ = document.Reading;

        // Assert.
        Assert.Equal(bytes, System.Text.Encoding.UTF8.GetBytes(document.Text));
    }

    [Theory]
    [MemberData(nameof(LineEndings))]
    public void TheSmallFlow_ReadsEveryEntry(string fixture)
    {
        // Act.
        var model = SankeyReader.Parse(Load(fixture).Text);

        // Assert.
        Assert.Empty(model.Problems);
        Assert.Equal(1, model.Version);
        Assert.Equal("{value} t", model.Settings.Format);
        Assert.Equal(2, model.Settings.FormatLine); // zero-based: `format:` is the third line
        Assert.Equal(SankeySettings.FromTarget, model.Settings.FlowColor);
        Assert.Equal(1, model.Settings.Thickness);
        Assert.Equal(["a", "b", "m", "x", "y"], model.Nodes.Select(node => node.Id));
        Assert.Equal(["a->m", "b->m", "m->x", "m->y"], model.Flows.Select(flow => flow.Id));

        Assert.Equal(("Source A", "blue"), (model.Nodes[0].Name, model.Nodes[0].Color));
        Assert.Equal("all of it", model.Nodes[2].Note);
        Assert.Equal("#336699", model.Nodes[3].Color);
        Assert.Equal((7d, 0.5d), (model.Flows[2].Value!.Value, model.Flows[2].Step!.Value));
        Assert.Null(model.Flows[3].Step);
    }

    [Fact]
    public void MalformedEntries_AreReported_AndTheRestStillReads()
    {
        // Act.
        var model = SankeyReader.Parse(Load("malformed-entries.skv").Text);

        // Assert: each reported, none of them fatal.
        Assert.Contains(model.Problems, problem => problem.Message.Contains("`thickness: wide`", StringComparison.Ordinal));
        Assert.Contains(model.Problems, problem => problem.Message.Contains("`flow-color: sideways`", StringComparison.Ordinal));
        Assert.Contains(model.Problems, problem => problem.Message.Contains("`colour`", StringComparison.Ordinal));
        Assert.Contains(model.Problems, problem => problem.Message.Contains("`column: 1.5`", StringComparison.Ordinal));
        Assert.Contains(model.Problems, problem => problem.Message.Contains("not a mapping", StringComparison.Ordinal));
        Assert.Contains(model.Problems, problem => problem.Message.Contains("`value: lots`", StringComparison.Ordinal));
        Assert.Equal(["a", "b"], model.Nodes.Select(node => node.Id));
        Assert.Null(model.Nodes[1].Column);
        Assert.Null(Assert.Single(model.Flows).Value);
        Assert.Equal(SankeySettings.Default.Thickness, model.Settings.Thickness);
        // A value refused is still a key written: the default is kept, and the key's line recorded.
        Assert.Equal(SankeySettings.FromTarget, model.Settings.FlowColor);
        Assert.Equal(2, model.Settings.FlowColorLine); // zero-based: `flow-color: sideways` is the third line
    }

    [Fact]
    public void TextThatIsNotYaml_IsOneProblem_AndAnEmptyModel()
    {
        // Act.
        var model = SankeyReader.Parse(Load("not-yaml.skv").Text);

        // Assert.
        Assert.Contains("could not be read as YAML", Assert.Single(model.Problems).Message, StringComparison.Ordinal);
        Assert.Empty(model.Nodes);
    }

    [Fact]
    public void AMissingHeader_IsReported()
    {
        // Act.
        var model = SankeyReader.Parse("nodes: []\n");

        // Assert.
        Assert.Contains(model.Problems, problem => problem.Message.Contains("does not begin with", StringComparison.Ordinal));
    }

    [Fact]
    public void SettingANumber_ChangesThatLineOnly()
    {
        // Arrange.
        var document = Body("lf-line-endings.skv");
        var before = Lines(document);
        var flow = document.Reading.Model.Flows[0];

        // Act.
        Assert.True(SankeyEdits.SetNumber(document, flow, "value", 12.5).WasApplied);

        // Assert.
        var after = Lines(document);
        var changed = Enumerable.Range(0, before.Count).Where(index => before[index] != after[index]).ToList();
        Assert.Equal("    value: 12.5", after[Assert.Single(changed)]);
        Assert.Equal(12.5, document.Reading.Model.Flows[0].Value);
    }

    [Theory]
    [InlineData("y", "x", false, new[] { "a", "b", "m", "y", "x" })]
    [InlineData("a", "b", true, new[] { "b", "a", "m", "x", "y" })]
    [InlineData("b", "a", false, new[] { "b", "a", "m", "x", "y" })]
    [InlineData("a", "y", true, new[] { "b", "m", "x", "y", "a" })]
    public void MovingANode_MovesItsLinesBesideTheAnchor_CommentsIncluded(string node, string anchor, bool after, string[] expected)
    {
        // Arrange.
        var document = Body("lf-line-endings.skv");
        var model = document.Reading.Model;
        var count = Lines(document).Count;

        // Act.
        var edit = SankeyEdits.MoveNode(document, model.Nodes.Single(entry => entry.Id == node), model.Nodes.Single(entry => entry.Id == anchor), after);

        // Assert.
        Assert.True(edit.WasApplied, edit.Refusal);
        Assert.Equal(count, Lines(document).Count);
        var moved = document.Reading.Model;
        Assert.Empty(moved.Problems);
        Assert.Equal(expected, moved.Nodes.Select(entry => entry.Id));
        Assert.Contains("    name: Source A   # a comment the writer must keep", Lines(document));
    }

    [Fact]
    public void AddingANodeBeforeAnother_PutsItThere_InTheDocumentsIndentation()
    {
        // Arrange.
        var document = Body("lf-line-endings.skv");
        var model = document.Reading.Model;

        // Act.
        var node = new SankeyNode("n", "New: node", "", "", "", 3, "", new LineRange(0, 0));
        Assert.True(SankeyEdits.AddNode(document, node, model.Nodes[3]).WasApplied);

        // Assert.
        var added = document.Reading.Model;
        Assert.Empty(added.Problems);
        Assert.Equal(["a", "b", "m", "n", "x", "y"], added.Nodes.Select(entry => entry.Id));
        Assert.Equal(("New: node", 3), (added.Nodes[3].Name, added.Nodes[3].Column!.Value));
    }

    [Theory]
    [InlineData("{value} TWh")]
    [InlineData("[EU]")]
    [InlineData("yes")]
    [InlineData("12")]
    [InlineData("a: b")]
    [InlineData("#hash")]
    [InlineData("'quoted'")]
    public void AText_ReadsBackAsItself(string value)
    {
        // Arrange.
        var document = SankeyBody.Parse("sankey: 1\nnodes:\n  - id: a\n    name: A\n");
        var node = document.Reading.Model.Nodes[0];

        // Act.
        Assert.True(SankeyEdits.SetText(document, node, "note", value, removeWhenEmpty: true).WasApplied);

        // Assert.
        var read = document.Reading.Model;
        Assert.Empty(read.Problems);
        Assert.Equal(value, read.Nodes[0].Note);
    }

    [Fact]
    public void ADocumentWideKey_IsWrittenWhereTheBindingOrdersIt_WhenTheDocumentStatesNone()
    {
        // Arrange.
        var document = SankeyBody.Parse("sankey: 1\nnodes: []\n");

        // Act.
        Assert.True(SankeyEdits.SetThickness(document, 1.25).WasApplied);

        // Assert: after the keys the binding orders before it, here the header alone (finding B12).
        Assert.Equal("sankey: 1\nthickness: 1.25\nnodes: []\n", document.Text);
    }

    [Theory]
    [InlineData("€{value}M", 1302.5, "€1,302.5M")]
    [InlineData("({value})", 3, "(3)")]
    [InlineData("", 0.25, "0.25")]
    [InlineData("people", 7, "7 people")]
    public void AValue_IsWrittenInItsFormat(string format, double value, string expected)
    {
        // Act.
        var written = SankeyFormat.Write(format, value);

        // Assert.
        Assert.Equal(expected, written);
    }
}
