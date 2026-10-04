using EtAlii.Adp.Documents;
using Xunit;

namespace EtAlii.Adp.Diagram.SupplyChain.Tests;

/// <summary>
/// The format: what the parser reads, what it reports instead of throwing, and that every write is
/// a splice which leaves every line it did not mean to touch exactly as it was.
/// </summary>
public sealed class SupplyChainDocumentTests
{
    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    private static LineDocument Load(string name) => LineDocument.Parse(File.ReadAllText(Fixture(name)));

    public static TheoryData<string> LineEndings => ["crlf-line-endings.supply", "lf-line-endings.supply", "no-trailing-newline.supply"];

    [Theory]
    [MemberData(nameof(LineEndings))]
    public async Task AnUnchangedDocument_ComesBackByteIdentical(string fixture)
    {
        // Arrange.
        var bytes = await File.ReadAllBytesAsync(Fixture(fixture), TestContext.Current.CancellationToken);

        // Act.
        var document = LineDocument.Parse(await File.ReadAllTextAsync(Fixture(fixture), TestContext.Current.CancellationToken));
        SupplyChainParser.Parse(document);

        // Assert.
        Assert.Equal(bytes, System.Text.Encoding.UTF8.GetBytes(document.Text));
    }

    [Theory]
    [MemberData(nameof(LineEndings))]
    public void TheSmallChain_ReadsEveryEntry(string fixture)
    {
        // Act.
        var model = SupplyChainParser.Parse(Load(fixture));

        // Assert.
        Assert.Empty(model.Problems);
        Assert.Equal(1, model.Version);
        Assert.Equal(["north"], model.Groups.Select(group => group.Id));
        Assert.Equal(["mine", "plant", "shop"], model.Nodes.Select(node => node.Id));
        Assert.Equal(["ore", "goods"], model.Flows.Select(flow => flow.Id));

        var mine = model.Nodes[0];
        Assert.Equal(SupplyChainNodeTypes.RawMaterial, mine.Type);
        Assert.Equal("Mine", mine.Name);
        Assert.Equal("north", mine.Group);
        Assert.Equal(10, mine.Quantity);
        Assert.Equal("t", mine.Unit);
        Assert.Null(mine.Step);
        Assert.False(mine.IsPlaced);

        Assert.Equal(0.5, model.Nodes[1].Step);
        Assert.True(model.Nodes[2].IsPlaced);
        Assert.Equal(800, model.Nodes[2].X);

        var ore = model.Flows[0];
        Assert.Equal(("mine", "plant", "Ore", 6d, "t"), (ore.From, ore.To, ore.Product, ore.Volume!.Value, ore.Unit));
    }

    [Fact]
    public void MalformedEntries_AreReported_AndTheRestStillReads()
    {
        // Act.
        var model = SupplyChainParser.Parse(Load("malformed-entries.supply"));

        // Assert: the unknown stage, the unknown key, the number that is not one and the entry that
        // is not a mapping - each reported, none of them fatal.
        Assert.Contains(model.Problems, problem => problem.Message.Contains("`quarry` is not a stage", StringComparison.Ordinal));
        Assert.Contains(model.Problems, problem => problem.Message.Contains("`colour`", StringComparison.Ordinal));
        Assert.Contains(model.Problems, problem => problem.Message.Contains("`quantity: lots`", StringComparison.Ordinal));
        Assert.Contains(model.Problems, problem => problem.Message.Contains("not a mapping", StringComparison.Ordinal));
        Assert.Equal(["mine", "plant"], model.Nodes.Select(node => node.Id));
        Assert.Null(model.Nodes[1].Quantity);
        Assert.Single(model.Flows);
    }

    [Fact]
    public void TextThatIsNotYaml_IsOneProblem_AndAnEmptyModel()
    {
        // Act.
        var model = SupplyChainParser.Parse(Load("not-yaml.supply"));

        // Assert.
        Assert.Contains("could not be read as YAML", Assert.Single(model.Problems).Message, StringComparison.Ordinal);
        Assert.Empty(model.Nodes);
    }

    [Fact]
    public void AMissingHeader_IsReported()
    {
        // Act.
        var model = SupplyChainParser.Parse(LineDocument.Parse("nodes: []\n"));

        // Assert.
        Assert.Contains(model.Problems, problem => problem.Message.Contains("does not begin with", StringComparison.Ordinal));
    }

    [Fact]
    public void SettingANumber_ChangesThatLineOnly()
    {
        // Arrange.
        var document = Load("lf-line-endings.supply");
        var before = document.Lines.Select(line => line.Text).ToList();
        var mine = SupplyChainParser.Parse(document).Nodes[0];

        // Act.
        Assert.True(SupplyChainWriter.SetNumber(document, mine.Range, "quantity", 12.5).WasApplied);

        // Assert.
        var changed = Enumerable.Range(0, before.Count).Where(index => before[index] != document.Lines[index].Text).ToList();
        Assert.Equal("    quantity: 12.5", document.Lines[Assert.Single(changed)].Text);
        Assert.Equal(12.5, SupplyChainParser.Parse(document).Nodes[0].Quantity);
    }

    [Fact]
    public void PlacingAnUnplacedNode_WritesXBeforeY_AndKeepsEveryOtherLine()
    {
        // Arrange.
        var document = Load("lf-line-endings.supply");
        var model = SupplyChainParser.Parse(document);

        // Act.
        Assert.True(SupplyChainWriter.Place(document, model, new Dictionary<string, (double X, double Y)> { ["mine"] = (100.4, 200.6), ["shop"] = (900, 50) }).WasApplied);

        // Assert.
        var reread = SupplyChainParser.Parse(document);
        Assert.Empty(reread.Problems);
        Assert.Equal((100d, 201d), (reread.Nodes[0].X!.Value, reread.Nodes[0].Y!.Value));
        Assert.Equal((900d, 50d), (reread.Nodes[2].X!.Value, reread.Nodes[2].Y!.Value));
        var x = document.Lines.Select(line => line.Text).ToList().IndexOf("    x: 100");
        Assert.True(x > 0);
        Assert.Equal("    y: 201", document.Lines[x + 1].Text);
        Assert.Contains(document.Lines, line => line.Text.Contains("# a comment the writer must keep", StringComparison.Ordinal));
    }

    [Fact]
    public void AddingANode_ToADocumentWithoutTheSection_BringsTheSection()
    {
        // Arrange.
        var document = LineDocument.Parse("supply-chain: 1\n");
        var model = SupplyChainParser.Parse(document);

        // Act.
        var node = new SupplyChainNode("n1", SupplyChainNodeTypes.Supplier, "Acme", "", "", 3, "", null, 10, 20, default);
        Assert.True(SupplyChainWriter.AddNode(document, model, node).WasApplied);

        // Assert.
        var reread = SupplyChainParser.Parse(document);
        Assert.Empty(reread.Problems);
        var added = Assert.Single(reread.Nodes);
        Assert.Equal(("n1", "Acme", 3d, 10d, 20d), (added.Id, added.Name, added.Quantity!.Value, added.X!.Value, added.Y!.Value));
    }

    [Theory]
    [InlineData("[EU]")]
    [InlineData("yes")]
    [InlineData("12")]
    [InlineData("Ore: crushed")]
    [InlineData("# not a comment")]
    [InlineData("null")]
    public void AName_ThatYamlWouldReadAsSomethingElse_ReadsBackAsWritten(string name)
    {
        // Arrange.
        var document = Load("lf-line-endings.supply");
        var mine = SupplyChainParser.Parse(document).Nodes[0];

        // Act.
        SupplyChainWriter.SetText(document, mine.Range, "name", name, removeWhenEmpty: false);

        // Assert.
        var reread = SupplyChainParser.Parse(document);
        Assert.Empty(reread.Problems);
        Assert.Equal(name, reread.Nodes[0].Name);
    }

    [Fact]
    public void RemovingAGroup_UngroupsItsMembers()
    {
        // Arrange.
        var document = Load("lf-line-endings.supply");
        var model = SupplyChainParser.Parse(document);

        // Act.
        SupplyChainWriter.RemoveGroup(document, model, model.Groups[0]);

        // Assert.
        var reread = SupplyChainParser.Parse(document);
        Assert.Empty(reread.Problems);
        Assert.Empty(reread.Groups);
        Assert.All(reread.Nodes, node => Assert.Equal("", node.Group));
    }

    [Fact]
    public void RemovingANode_RemovesItsFlows()
    {
        // Arrange.
        var document = Load("lf-line-endings.supply");
        var model = SupplyChainParser.Parse(document);

        // Act.
        SupplyChainWriter.RemoveNode(document, model, model.Nodes[1]);

        // Assert.
        var reread = SupplyChainParser.Parse(document);
        Assert.Equal(["mine", "shop"], reread.Nodes.Select(node => node.Id));
        Assert.Empty(reread.Flows);
    }
}
