using EtAlii.Adp.Documents;
using Xunit;

namespace EtAlii.Adp.Diagram.AgentBehaviorModelling.Tests;

public class AbmWriterTests
{
    private const string Tree =
        "# Agent\r\n" +
        "\r\n" +
        "Prose the module never touches.\r\n" +
        "\r\n" +
        "## Behavior\r\n" +
        "\r\n" +
        "- **Do in order:** Work\r\n" +
        "  - **Check:** Ready\r\n" +
        "  - **Try in order:** Options\r\n" +
        "    - **Do:** First\r\n" +
        "      Notes for first.\r\n" +
        "    - **Do:** Second\r\n" +
        "  - **Do:** Finish\r\n" +
        "\r\n" +
        "## After\r\n" +
        "\r\n" +
        "More prose.";

    private static (LineDocument Document, AbmModel Model) Load(string text = Tree)
    {
        var document = LineDocument.Parse(text);
        return (document, AbmParser.Parse(document));
    }

    [Theory]
    [InlineData(Tree)]
    [InlineData("## Behavior\n- **Do in order:** Work\n  - **Do:** Act\n")]
    [InlineData("## Behavior\n\n- **Do in order:** Work\n\n  - **Do:** Act")]
    public void ADocumentNothingEdited_ComesBackByteForByte(string text)
    {
        // Act.
        (LineDocument document, _) = Load(text);

        // Assert.
        Assert.Equal(text, document.Text);
    }

    [Fact]
    public void SetLabel_RewritesOneLine()
    {
        // Arrange.
        (LineDocument document, AbmModel model) = Load();

        // Act.
        var edit = AbmWriter.SetLabel(document, model.NodeOf("1.2.1")!, "Try the cache\nfirst");

        // Assert.
        Assert.True(edit.WasApplied);
        Assert.Equal(Tree.Replace("**Do:** First", "**Do:** Try the cache first", StringComparison.Ordinal), document.Text);
    }

    [Fact]
    public void SetKind_WritesTheKeyword_AndARetrysCount()
    {
        // Arrange.
        (LineDocument document, AbmModel model) = Load();

        // Act.
        AbmWriter.SetKind(document, model.NodeOf("1.2")!, AbmNodeKinds.Parallel, 0);
        AbmWriter.SetKind(document, model.NodeOf("1.3")!, AbmNodeKinds.Ask, 0);

        // Assert.
        Assert.Contains("  - **Do together:** Options\r\n", document.Text, StringComparison.Ordinal);
        Assert.Contains("  - **Ask the user:** Finish\r\n", document.Text, StringComparison.Ordinal);
        Assert.Equal("Retry up to 1 time", AbmNodeKinds.KeywordOf(AbmNodeKinds.Retry, 1));
    }

    [Fact]
    public void SetKind_RefusesAKindThatCannotHoldTheChildren()
    {
        // Arrange.
        (LineDocument document, AbmModel model) = Load();

        // Act.
        var toLeaf = AbmWriter.SetKind(document, model.NodeOf("1.2")!, AbmNodeKinds.Action, 0);
        var toDecorator = AbmWriter.SetKind(document, model.NodeOf("1.2")!, AbmNodeKinds.Guard, 0);

        // Assert.
        Assert.False(toLeaf.WasApplied);
        Assert.False(toDecorator.WasApplied);
        Assert.Equal(Tree, document.Text);
    }

    [Fact]
    public void SetNotes_AddsReplacesAndRemoves()
    {
        // Arrange.
        (LineDocument document, AbmModel model) = Load();

        // Act: add.
        AbmWriter.SetNotes(document, model.NodeOf("1.3")!, "Say you are done.\n\nThen stop.");

        // Assert.
        Assert.Contains("  - **Do:** Finish\r\n    Say you are done.\r\n\r\n    Then stop.\r\n", document.Text, StringComparison.Ordinal);

        // Act: remove the ones that were there.
        var reread = AbmParser.Parse(document);
        AbmWriter.SetNotes(document, reread.NodeOf("1.2.1")!, "");

        // Assert.
        Assert.DoesNotContain("Notes for first.", document.Text, StringComparison.Ordinal);
        Assert.Equal("Say you are done.\n\nThen stop.", AbmParser.Parse(document).NodeOf("1.3")!.Notes);
    }

    [Fact]
    public void Add_PutsAChildWhereItsSiblingsAre()
    {
        // Arrange.
        (LineDocument document, AbmModel model) = Load();

        // Act.
        (AbmEdit edit, string id) = AbmWriter.Add(document, model, model.NodeOf("1.2")!, 1, AbmNodeKinds.Check, "Cached");

        // Assert.
        Assert.True(edit.WasApplied);
        Assert.Equal("1.2.2", id);
        var reread = AbmParser.Parse(document);
        Assert.Equal("Cached", reread.NodeOf("1.2.2")!.Label);
        Assert.Equal("Second", reread.NodeOf("1.2.3")!.Label);
        Assert.Contains("      Notes for first.\r\n    - **Check:** Cached\r\n", document.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Add_TheFirstChild_StartsAtTheParentsTextColumn()
    {
        // Arrange.
        (LineDocument document, AbmModel model) = Load();

        // Act.
        AbmWriter.Add(document, model, model.NodeOf("1")!, -1, AbmNodeKinds.Action, "Last");
        var reread = AbmParser.Parse(document);
        (AbmEdit edit, _) = AbmWriter.Add(document, reread, reread.NodeOf("1.3")!, -1, AbmNodeKinds.Action, "Under a leaf");

        // Assert: appended after the whole subtree, and a leaf takes no child.
        Assert.Equal("Last", AbmParser.Parse(document).NodeOf("1.4")!.Label);
        Assert.False(edit.WasApplied);
    }

    [Fact]
    public void Add_TheFirstRoot_StartsABehaviorSectionWhenThereIsNone()
    {
        // Arrange.
        (LineDocument document, AbmModel model) = Load("# Agent\r\n\r\nNo tree yet.\r\n");

        // Act.
        (AbmEdit edit, string id) = AbmWriter.Add(document, model, null, -1, AbmNodeKinds.Sequence, "Handle the request");

        // Assert.
        Assert.True(edit.WasApplied);
        Assert.Equal("1", id);
        Assert.Equal("# Agent\r\n\r\nNo tree yet.\r\n\r\n## Behavior\r\n\r\n- **Do in order:** Handle the request\r\n", document.Text);
    }

    [Fact]
    public void Add_TheFirstRoot_GoesUnderAnExistingHeading()
    {
        // Arrange.
        (LineDocument document, AbmModel model) = Load("## Behavior\n\nNothing yet.\n");

        // Act.
        AbmWriter.Add(document, model, null, -1, AbmNodeKinds.Action, "Start");

        // Assert.
        Assert.Equal("Start", Assert.Single(AbmParser.Parse(document).Nodes).Label);
    }

    [Fact]
    public void Remove_TakesTheSubtreeAndItsNotes()
    {
        // Arrange.
        (LineDocument document, AbmModel model) = Load();

        // Act.
        AbmWriter.Remove(document, model.NodeOf("1.2")!);

        // Assert.
        Assert.Equal(
            Tree.Replace("  - **Try in order:** Options\r\n    - **Do:** First\r\n      Notes for first.\r\n    - **Do:** Second\r\n", "", StringComparison.Ordinal),
            document.Text);
    }

    [Fact]
    public void Move_EarlierAmongSiblings_SwapsTheSubtrees()
    {
        // Arrange.
        (LineDocument document, AbmModel model) = Load();

        // Act: Options (index 1) one earlier.
        var edit = AbmWriter.Move(document, model, model.NodeOf("1.2")!, model.NodeOf("1")!, 0);

        // Assert.
        Assert.True(edit.WasApplied);
        var reread = AbmParser.Parse(document);
        Assert.Equal(["Options", "Ready", "Finish"], reread.ChildrenOf(reread.NodeOf("1")!).Select(node => node.Label));
        Assert.Equal("Notes for first.", reread.NodeOf("1.1.1")!.Notes);
    }

    [Fact]
    public void Move_UnderAnotherParent_ReIndentsTheSubtree()
    {
        // Arrange.
        (LineDocument document, AbmModel model) = Load();

        // Act: Finish under Options, last.
        AbmWriter.Move(document, model, model.NodeOf("1.3")!, model.NodeOf("1.2")!, -1);

        // Assert.
        Assert.Contains("    - **Do:** Second\r\n    - **Do:** Finish\r\n", document.Text, StringComparison.Ordinal);

        // Act: Options to the root level, re-indented two columns left, notes included.
        var reread = AbmParser.Parse(document);
        AbmWriter.Move(document, reread, reread.NodeOf("1.2")!, null, -1);

        // Assert.
        Assert.Contains("- **Try in order:** Options\r\n  - **Do:** First\r\n    Notes for first.\r\n", document.Text, StringComparison.Ordinal);
        Assert.Equal(2, AbmParser.Parse(document).Roots.Count);
    }

    [Fact]
    public void Move_BeneathItself_IsRefused()
    {
        // Arrange.
        (LineDocument document, AbmModel model) = Load();

        // Act.
        var edit = AbmWriter.Move(document, model, model.NodeOf("1")!, model.NodeOf("1.2")!, -1);

        // Assert.
        Assert.False(edit.WasApplied);
        Assert.Equal(Tree, document.Text);
    }

    [Fact]
    public void Move_ToWhereItAlreadyIs_IsRefused()
    {
        // Arrange.
        (LineDocument document, AbmModel model) = Load();

        // Act.
        var edit = AbmWriter.Move(document, model, model.NodeOf("1.2")!, model.NodeOf("1")!, 2);

        // Assert.
        Assert.False(edit.WasApplied);
    }
}
