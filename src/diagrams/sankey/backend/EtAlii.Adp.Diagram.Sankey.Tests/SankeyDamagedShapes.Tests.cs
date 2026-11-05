using EtAlii.Adp.Diagram.Sankey.Tests.Parity;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Files;
using EtAlii.Adp.History;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EtAlii.Adp.Diagram.Sankey.Tests;

/// <summary>
/// The four shapes the reader reads and the line writer damaged (sankey-disl-fbl finding B16, Requirement 5.10):
/// each is edited without damage, or refused with a sentence and left as it was. Each guard was seen to fail against
/// <c>SankeyWriter</c> before the commands wrote through the binding, and the damage it found is shown here against
/// that writer, kept as <see cref="HandWrittenSankeyEdits"/>.
/// </summary>
public sealed class SankeyDamagedShapesTests
{
    [Fact]
    public async Task ABlockScalarLineThatStartsWithAKey_IsNotTakenForTheKey()
    {
        // Arrange: node a's description holds a line that starts with name:.
        using var folder = Folder("block-scalar-key-line.skv");
        var before = Model(folder);

        // Act.
        var result = await Dispatch(folder, new SetSankeyPropertyCommand(folder.Body, "a", SankeyKeys.Name, "Renamed"));

        // Assert: the name is a's own key, and the description is what it was (the writer rewrote the description's line).
        AssertEditedOrRefused(folder, result, after =>
        {
            Assert.Equal("Renamed", after.NodeOf("a")!.Name);
            Assert.Equal(before.NodeOf("a")!.Description, after.NodeOf("a")!.Description);
        });
    }

    [Fact]
    public async Task AnEntryWrittenAsAFlowMapping_StaysReadable()
    {
        // Arrange: - {id: a, name: A}.
        using var folder = Folder("flow-mapping-entry.skv");

        // Act.
        var result = await Dispatch(folder, new SetSankeyPropertyCommand(folder.Body, "a", SankeyKeys.Name, "Renamed"));

        // Assert: the writer put a block line under the flow mapping, and the document no longer parsed.
        AssertEditedOrRefused(folder, result, after => Assert.Equal("Renamed", after.NodeOf("a")!.Name));
    }

    [Fact]
    public async Task AFirstKeyAfterAWideDashGap_IsFound_AndNotWrittenTwice()
    {
        // Arrange: -   name: A.
        using var folder = Folder("wide-dash-gap.skv");

        // Act.
        var result = await Dispatch(folder, new SetSankeyPropertyCommand(folder.Body, "a", SankeyKeys.Name, "Renamed"));

        // Assert: the writer missed the first key, wrote a second name:, and the duplicate made the document unreadable (B9).
        AssertEditedOrRefused(folder, result, after => Assert.Equal("Renamed", after.NodeOf("a")!.Name));
    }

    [Fact]
    public async Task ALineThatStartsWithASectionKeyInsideAValue_IsNotTakenForTheSection()
    {
        // Arrange: the format's block scalar holds a line that starts with nodes:.
        using var folder = Folder("section-key-in-value.skv");
        var before = Model(folder);

        // Act.
        var result = await Dispatch(folder, new AddSankeyNodeCommand(folder.Body, 0, 0, "n"));

        // Assert: the node is a node, and the format is what it was (the writer wrote the node into the format).
        AssertEditedOrRefused(folder, result, after =>
        {
            Assert.Equal("New node", after.NodeOf("n")?.Name);
            Assert.Equal(before.Settings.Format, after.Settings.Format);
        });
    }

    [Fact]
    public void TheLineWriter_TookTheBlockScalarLineForTheKey()
    {
        // Arrange.
        (LineDocument document, SankeyModel model) = Oracle("block-scalar-key-line.skv");

        // Act.
        HandWrittenSankeyEdits.SetText(document, model.NodeOf("a")!.Range, SankeyKeys.Name, "Renamed", removeWhenEmpty: false);

        // Assert: the description's line was rewritten, and the name was not.
        var after = SankeyReader.Parse(document.Text);
        Assert.Equal("A", after.NodeOf("a")!.Name);
        Assert.NotEqual(model.NodeOf("a")!.Description, after.NodeOf("a")!.Description);
    }

    [Theory]
    [InlineData("flow-mapping-entry.skv", "did not find expected '-' indicator")]
    [InlineData("wide-dash-gap.skv", "Duplicate key")]
    public void TheLineWriter_LeftADocumentThatIsNotYaml(string fixture, string reason)
    {
        // Arrange.
        (LineDocument document, SankeyModel model) = Oracle(fixture);

        // Act.
        HandWrittenSankeyEdits.SetText(document, model.NodeOf("a")!.Range, SankeyKeys.Name, "Renamed", removeWhenEmpty: false);

        // Assert.
        Assert.Contains(reason, SankeyReader.Outcome(document.Text).Unreadable, StringComparison.Ordinal);
    }

    [Fact]
    public void TheLineWriter_WroteANodeIntoTheValueThatHoldsASectionKey()
    {
        // Arrange.
        (LineDocument document, SankeyModel model) = Oracle("section-key-in-value.skv");

        // Act.
        HandWrittenSankeyEdits.AddNode(document, model, new SankeyNode("n", "New node", "", "", "", null, "", new LineRange(0, 0)));

        // Assert: the node is part of the format, and no node.
        var after = SankeyReader.Parse(document.Text);
        Assert.Null(after.NodeOf("n"));
        Assert.Contains("id: n", after.Settings.Format, StringComparison.Ordinal);
    }

    private static (LineDocument Document, SankeyModel Model) Oracle(string fixture)
    {
        var document = LineDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture)));
        return (document, HandWrittenSankeyReader.Outcome(document).Model);
    }

    private static SankeyTestFolder Folder(string fixture) =>
        new(Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture), fixture);

    private static SankeyModel Model(SankeyTestFolder folder) => SankeyReader.Parse(File.ReadAllText(folder.Body));

    private static async Task<(CommandResult Result, byte[] Before)> Dispatch(SankeyTestFolder folder, ICommand command)
    {
        var before = await File.ReadAllBytesAsync(folder.Body, TestContext.Current.CancellationToken);
        var store = new SankeyDocumentStore(new FileWriter());
        var dispatcher = TestHistory.Dispatcher(services => { services.AddSankey(); services.AddSingleton<ISankeyDocumentStore>(store); });
        return (await dispatcher.DispatchAsync(command, TestContext.Current.CancellationToken), before);
    }

    /// <summary>An edit that was applied left a document that reads cleanly and says what <paramref name="edited"/> checks; one refused said why and wrote nothing.</summary>
    private static void AssertEditedOrRefused(SankeyTestFolder folder, (CommandResult Result, byte[] Before) run, Action<SankeyModel> edited)
    {
        var text = File.ReadAllText(folder.Body);
        if (!run.Result.IsSuccess)
        {
            Assert.False(string.IsNullOrWhiteSpace(run.Result.Error));
            Assert.Equal(run.Before, File.ReadAllBytes(folder.Body));
            return;
        }

        var outcome = SankeyReader.Outcome(text);
        Assert.True(outcome.IsUsable, outcome.Unreadable);
        Assert.Equal(SankeyReader.Outcome(System.Text.Encoding.UTF8.GetString(run.Before)).Problems.Count, outcome.Problems.Count);
        edited(outcome.Model);
    }
}
