using EtAlii.Adp.Documents;
using EtAlii.Adp.Specification.Disl;
using Xunit;

namespace EtAlii.Adp.Diagram.SupplyChain.Tests;

/// <summary>
/// The format: what the reading reads, what it reports instead of throwing, and that every write is
/// a splice which leaves every line it did not mean to touch exactly as it was.
/// </summary>
public sealed class SupplyChainDocumentTests
{
    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    private static SupplyChainBody Load(string name) => SupplyChainBody.Parse(File.ReadAllText(Fixture(name)));

    public static TheoryData<string> LineEndings => ["crlf-line-endings.supply", "lf-line-endings.supply", "no-trailing-newline.supply"];

    [Theory]
    [MemberData(nameof(LineEndings))]
    public async Task AnUnchangedDocument_ComesBackByteIdentical(string fixture)
    {
        // Arrange.
        var bytes = await File.ReadAllBytesAsync(Fixture(fixture), TestContext.Current.CancellationToken);

        // Act.
        var document = SupplyChainBody.Parse(await File.ReadAllTextAsync(Fixture(fixture), TestContext.Current.CancellationToken));
        _ = document.Reading;

        // Assert.
        Assert.Equal(bytes, System.Text.Encoding.UTF8.GetBytes(document.Text));
    }

    [Theory]
    [MemberData(nameof(LineEndings))]
    public void TheSmallChain_ReadsEveryEntry(string fixture)
    {
        // Act.
        var model = SupplyChainReading.Parse(Load(fixture).Text);

        // Assert.
        Assert.Empty(model.Problems);
        Assert.Equal(1, model.Version);
        Assert.Equal(["north"], model.Groups.Select(group => group.Id));
        Assert.Equal(["mine", "plant", "shop"], model.Nodes.Select(node => node.Id));
        Assert.Equal(["ore", "goods"], model.Flows.Select(flow => flow.Id));

        var mine = model.Nodes[0];
        Assert.Equal(SupplyChainNodeTypes.Source, mine.Type);
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
        var model = SupplyChainReading.Parse(Load("malformed-entries.supply").Text);

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
        var model = SupplyChainReading.Parse(Load("not-yaml.supply").Text);

        // Assert.
        Assert.Contains("could not be read as YAML", Assert.Single(model.Problems).Message, StringComparison.Ordinal);
        Assert.Empty(model.Nodes);
    }

    [Fact]
    public void AMissingHeader_IsReported()
    {
        // Act.
        var model = SupplyChainReading.Parse("nodes: []\n");

        // Assert.
        Assert.Contains(model.Problems, problem => problem.Message.Contains("does not begin with", StringComparison.Ordinal));
    }

    [Fact]
    public void SettingANumber_ChangesThatLineOnly()
    {
        // Arrange.
        var body = Load("lf-line-endings.supply");
        var before = Lines(body);

        // Act.
        Assert.True(SupplyChainDefinition.Set(body, body.Reading.Model, "mine", "quantity", "12.5")?.WasApplied == true);

        // Assert.
        var after = Lines(body);
        var changed = Enumerable.Range(0, before.Count).Where(index => before[index] != after[index]).ToList();
        Assert.Equal("    quantity: 12.5", after[Assert.Single(changed)]);
        Assert.Equal(12.5, SupplyChainReading.Parse(body.Text).Nodes[0].Quantity);
    }

    [Fact]
    public void PlacingAnUnplacedNode_WritesXBeforeY_AndKeepsEveryOtherLine()
    {
        // Arrange.
        var body = Load("lf-line-endings.supply");

        // Act.
        Assert.True(SupplyChainDefinition.Place(body, body.Reading.Model, new Dictionary<string, (double X, double Y)> { ["mine"] = (100.4, 200.6), ["shop"] = (900, 50) }).WasApplied);

        // Assert.
        var reread = SupplyChainReading.Parse(body.Text);
        Assert.Empty(reread.Problems);
        Assert.Equal((100d, 201d), (reread.Nodes[0].X!.Value, reread.Nodes[0].Y!.Value));
        Assert.Equal((900d, 50d), (reread.Nodes[2].X!.Value, reread.Nodes[2].Y!.Value));
        var lines = Lines(body);
        var x = lines.IndexOf("    x: 100");
        Assert.True(x > 0);
        Assert.Equal("    y: 201", lines[x + 1]);
        Assert.Contains(lines, line => line.Contains("# a comment the writer must keep", StringComparison.Ordinal));
    }

    [Fact]
    public void AddingANode_ToADocumentWithoutTheSection_BringsTheSection()
    {
        // Arrange.
        var body = SupplyChainBody.Parse("supply-chain: 1\n");

        // Act.
        Assert.True(SupplyChainDefinition.Run(body, body.Reading.Model, "addProcessorHere", null, DislIds.Fixed("n1"), SupplyChainDefinition.At(10 + (SupplyChainGeometry.NodeWidth / 2), 20 + (SupplyChainGeometry.NodeHeight / 2))).WasApplied);

        // Assert.
        var reread = SupplyChainReading.Parse(body.Text);
        Assert.Empty(reread.Problems);
        var added = Assert.Single(reread.Nodes);
        Assert.Equal(("n1", SupplyChainNodeTypes.Processor, 10d, 20d), (added.Id, added.Type, added.X!.Value, added.Y!.Value));
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
        var body = Load("lf-line-endings.supply");

        // Act.
        Assert.True(SupplyChainDefinition.Set(body, body.Reading.Model, "mine", "name", name)?.WasApplied == true);

        // Assert.
        var reread = SupplyChainReading.Parse(body.Text);
        Assert.Empty(reread.Problems);
        Assert.Equal(name, reread.Nodes[0].Name);
    }

    [Fact]
    public void RemovingAGroup_UngroupsItsMembers()
    {
        // Arrange.
        var body = Load("lf-line-endings.supply");
        var model = body.Reading.Model;

        // Act.
        Assert.True(SupplyChainDefinition.Remove(body, model, model.Groups[0].Id)?.WasApplied == true);

        // Assert.
        var reread = SupplyChainReading.Parse(body.Text);
        Assert.Empty(reread.Problems);
        Assert.Empty(reread.Groups);
        Assert.All(reread.Nodes, node => Assert.Equal("", node.Group));
    }

    [Fact]
    public void RemovingANode_RemovesItsFlows()
    {
        // Arrange.
        var body = Load("lf-line-endings.supply");
        var model = body.Reading.Model;

        // Act.
        Assert.True(SupplyChainDefinition.Remove(body, model, model.Nodes[1].Id)?.WasApplied == true);

        // Assert.
        var reread = SupplyChainReading.Parse(body.Text);
        Assert.Equal(["mine", "shop"], reread.Nodes.Select(node => node.Id));
        Assert.Empty(reread.Flows);
    }

    private static List<string> Lines(SupplyChainBody body) => [.. body.Text.Split('\n').Select(line => line.TrimEnd('\r'))];
}
