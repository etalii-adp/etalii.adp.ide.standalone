using Xunit;

namespace EtAlii.Adp.Diagram.Mindmap.Tests;

/// <summary>
/// The document against the real corpus: what it reads, what it writes back, and what its
/// edits do to the XML behind the tree.
/// </summary>
public class MindmapDocumentTests
{
    private const string Map = "Fixtures/architecture.mm";

    private static string Corpus() => File.ReadAllText(Map);

    private static MindmapDocument Load(bool assignMissingIds = true) => MindmapDocument.Parse(Corpus(), assignMissingIds);

    // ---- round trip: the requirement that matters most -------------------------------------

    [Fact]
    public void ParseThenWrite_ReproducesTheCorpusByteForByte()
    {
        // Arrange.
        // Requirement 3.3. Without id assignment, because two corpus nodes have none and
        // assigning them is the one change the spec allows on a file ADP did not edit.
        var original = Corpus();

        // Act.
        var written = Load(assignMissingIds: false).ToText();

        // Assert.
        Assert.Equal(original, written);
    }

    [Fact]
    public void ParseThenWrite_IsStableAcrossASecondPass()
    {
        // Arrange.
        var once = Load().ToText();

        // Act.
        var twice = MindmapDocument.Parse(once).ToText();

        // Assert.
        Assert.Equal(once, twice);
    }

    [Fact]
    public void ParseThenWrite_KeepsAnElementAndAnAttributeItDoesNotUnderstand()
    {
        // Arrange.
        // Requirement 3.2: something a future Freeplane adds survives a trip through ADP.
        var original = Corpus()
            .Replace("<node TEXT=\"Backend\"", "<node TEXT=\"Backend\" FUTURE_ATTR=\"kept\"")
            .Replace("<edge COLOR=\"#ff0000\" WIDTH=\"2\"/>", "<edge COLOR=\"#ff0000\" WIDTH=\"2\"/>\n<future_element answer=\"42\"><inner/></future_element>");

        // Act.
        var written = MindmapDocument.Parse(original, assignMissingIds: false).ToText();

        // Assert.
        Assert.Equal(original, written);
    }

    [Fact]
    public void ParseThenWrite_KeepsMixedLineEndings_AndAnEscapedCarriageReturnInAnAttribute()
    {
        // Arrange.
        // The corpus covers the real thing at scale; this pins the rule in miniature: CRLF
        // structure and an LF line coexist untouched, and a CR in an attribute stays escaped
        // rather than becoming a raw byte the next parser would fold into a space.
        const string mixed = "<map version=\"freeplane 1.12.15\">\r\n" +
            "<node TEXT=\"a&#xd;b\" ID=\"ID_1\">\r\n" +
            "<node TEXT=\"lf line\" ID=\"ID_2\"/>\n" +
            "</node>\r\n" +
            "</map>\r\n";

        // Act.
        var written = MindmapDocument.Parse(mixed, assignMissingIds: false).ToText();

        // Assert.
        Assert.Equal(mixed, written);
    }

    [Fact]
    public void AssigningMissingIds_ChangesOnlyTheNodesThatHadNone()
    {
        // Arrange.
        // Deliberately hand-written: a genuine Freeplane save always carries IDs (the corpus
        // guard asserts exactly that), so the one place an ID-less node exists is a file no
        // Freeplane has saved yet - which is the case this tolerance is for.
        const string handWritten = """
            <map version="freeplane 1.11.5">
            <node TEXT="root" ID="ID_1">
            <node TEXT="no id yet">
            <node TEXT="child without one either"/>
            </node>
            </node>
            </map>
            """;

        // Act.
        var document = MindmapDocument.Parse(handWritten);

        // Assert.
        Assert.All(document.Nodes, node => Assert.NotEqual("", node.Id));
        var assigned = document.Nodes.Single(node => node.Text == "no id yet");
        Assert.StartsWith("ID_", assigned.Id, StringComparison.Ordinal);
        Assert.Equal("ID_1", assigned.Parent!.Id); // an id the file already had is untouched
        // The assignment is persisted on the next save, not only held in memory.
        Assert.Contains($"ID=\"{assigned.Id}\"", document.ToText(), StringComparison.Ordinal);
    }

    // ---- reading ------------------------------------------------------------------------------

    [Fact]
    public void Parse_ReadsTheTree()
    {
        // Act.
        var document = Load();

        // Assert.
        Assert.Equal("ADP architecture", document.Root.Text);
        Assert.Equal(4, document.Root.Children.Count);
        Assert.Equal(["Backend", "Client", "Diagram types", "Deliberately deep"], document.Root.Children.Select(child => child.Text));
        Assert.Equal(25, document.Nodes.Count());
    }

    [Fact]
    public void Parse_ReadsTextHeldAsRichContent()
    {
        // Arrange.
        var document = Load();

        // Act.
        var rich = document.Find("ID_411002940")!;

        // Assert.
        Assert.Equal("A node whose text is rich content rather than a TEXT attribute", rich.Text);
    }

    [Fact]
    public void Parse_ReadsNotes_WithAndWithoutAContentType()
    {
        // Act.
        var document = Load();

        // Assert.
        Assert.StartsWith("The map ADP's round-trip tests read.", document.Root.Notes, StringComparison.Ordinal);
        Assert.Contains("<node> is not a node.", document.Find("ID_88117420")!.Notes, StringComparison.Ordinal);
        Assert.Equal("", document.Find("ID_88117422")!.Notes);
    }

    [Fact]
    public void Parse_ReadsFoldLinkAndEmptyText()
    {
        // Act.
        var document = Load();

        // Assert.
        Assert.True(document.Find("ID_88117425")!.Folded);
        Assert.Equal("../../../../backend/EtAlii.Adp.Backend/Context/ContextServiceImpl.cs", document.Find("ID_88117420")!.Link);
        Assert.Null(document.Find("ID_88117422")!.Link);
        Assert.Equal("", document.Find("ID_411002939")!.Text);
    }

    [Fact]
    public void Parse_AMalformedFile_ThrowsNamingTheProblem()
    {
        // Act.
        var exception = Assert.Throws<MindmapFormatException>(() => MindmapDocument.Parse("<map><node TEXT='unclosed'></map>"));

        // Assert.
        Assert.Contains("well-formed", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_AFileThatIsNotAMap_Throws()
    {
        // Arrange, act and assert.
        Assert.Throws<MindmapFormatException>(() => MindmapDocument.Parse("<html><body/></html>"));
    }

    [Fact]
    public void Parse_AMapWithTwoRoots_Throws()
    {
        // Arrange, act and assert.
        Assert.Throws<MindmapFormatException>(() => MindmapDocument.Parse("<map><node TEXT=\"a\"/><node TEXT=\"b\"/></map>"));
    }

    // ---- structural edits ---------------------------------------------------------------------

    [Fact]
    public void AddChild_AppendsAsTheLastChild_AndWritesOnePerLine()
    {
        // Arrange.
        var document = Load();
        var backend = document.Find("ID_411002937")!;

        // Act.
        var added = document.AddChild(backend, "Logging");

        // Assert.
        Assert.Equal("Logging", backend.Children[^1].Text);
        Assert.StartsWith("ID_", added.Id, StringComparison.Ordinal);
        // The corpus is a genuine Windows Freeplane save, so a synthesized structural newline
        // matches its CRLF - an inserted line must not be the one LF line in the file.
        Assert.Contains("<node TEXT=\"Logging\" ID=\"" + added.Id + "\"/>\r\n</node>", document.ToText(), StringComparison.Ordinal);
    }

    [Fact]
    public void AddSibling_InsertsRightAfterTheNode()
    {
        // Arrange.
        var document = Load();
        var contextService = document.Find("ID_88117420")!;

        // Act.
        document.AddSibling(contextService, "Projects");

        // Assert.
        Assert.Equal(["Context service", "Projects", "Commands & history", "Hierarchy"], contextService.Parent!.Children.Select(child => child.Text));
    }

    [Fact]
    public void Move_CarriesTheWholeSubtree_ToTheGivenParentAndIndex()
    {
        // Arrange.
        var document = Load();
        var hierarchy = document.Find("ID_88117425")!;
        var client = document.Find("ID_411002938")!;

        // Act.
        document.Move(hierarchy, client, 0);

        // Assert.
        Assert.Equal("Hierarchy", client.Children[0].Text);
        Assert.Equal(2, client.Children[0].Children.Count); // HierarchyModel and RootFolderWatcher came along
        Assert.DoesNotContain(document.Find("ID_411002937")!.Children, child => child.Id == "ID_88117425");
    }

    [Fact]
    public void Move_IntoItsOwnSubtree_IsRefused()
    {
        // Arrange and act.
        var document = Load();
        var backend = document.Find("ID_411002937")!;
        var grandchild = document.Find("ID_88117422")!;

        // Assert.
        Assert.Throws<InvalidOperationException>(() => document.Move(backend, grandchild, 0));
    }

    [Fact]
    public void Move_TheRoot_IsRefused()
    {
        // Act.
        var document = Load();

        // Assert.
        Assert.Throws<InvalidOperationException>(() => document.Move(document.Root, document.Find("ID_411002937")!, 0));
    }

    [Fact]
    public void RemoveThenRestore_PutsTheSubtreeBackExactly()
    {
        // Arrange.
        var document = Load();
        var before = document.ToText();
        var hierarchy = document.Find("ID_88117425")!;
        var parent = hierarchy.Parent!;
        var index = hierarchy.IndexInParent;

        // Act.
        var subtree = document.Remove(hierarchy);
        Assert.Null(document.Find("ID_88117425"));
        Assert.Null(document.Find("ID_88117428")); // a grandchild went with it
        document.Restore(subtree, parent, index);

        // Assert.
        Assert.Equal(before, document.ToText());
    }

    [Fact]
    public void Remove_TheRoot_IsRefused()
    {
        // Act.
        var document = Load();

        // Assert.
        Assert.Throws<InvalidOperationException>(() => document.Remove(document.Root));
    }

    // ---- content edits -------------------------------------------------------------------------

    [Fact]
    public void SetText_OnARichContentNode_ReplacesTheMarkupWithPlainText()
    {
        // Arrange.
        var document = Load();
        var rich = document.Find("ID_411002940")!;

        // Act.
        document.SetText(rich, "plain now");

        // Assert.
        Assert.Equal("plain now", rich.Text);
        Assert.DoesNotContain("rich content", document.ToText(), StringComparison.Ordinal);
    }

    [Fact]
    public void SetText_ToEmpty_IsKept()
    {
        // Arrange.
        // Requirement 7.6: a node is not required to have text.
        var document = Load();
        var node = document.Find("ID_88117422")!;

        // Act.
        document.SetText(node, "");

        // Assert.
        Assert.Equal("", node.Text);
        Assert.Contains("TEXT=\"\" ID=\"ID_88117422\"", document.ToText(), StringComparison.Ordinal);
    }

    [Fact]
    public void SetNotes_AddsThenUpdatesThenRemovesTheNote()
    {
        // Arrange.
        var document = Load();
        var node = document.Find("ID_88117422")!;

        // Act and assert, step by step.
        document.SetNotes(node, "first line\nsecond line");
        Assert.Equal("first line\nsecond line", node.Notes);

        document.SetNotes(node, "changed");
        Assert.Equal("changed", node.Notes);
        Assert.Single(node.Element.Elements("richcontent"));

        document.SetNotes(node, "");
        Assert.Equal("", node.Notes);
        Assert.Empty(node.Element.Elements("richcontent"));
    }

    [Fact]
    public void SetLink_SetsAndClearsTheAttribute()
    {
        // Arrange.
        var document = Load();
        var node = document.Find("ID_88117422")!;

        // Act and assert, step by step.
        document.SetLink(node, "../file.cs");
        Assert.Equal("../file.cs", node.Link);

        document.SetLink(node, null);
        Assert.Null(node.Link);
    }

    [Fact]
    public void Edits_EscapeWhatXmlRequires_AndNothingElse()
    {
        // Arrange.
        var document = Load();
        var node = document.Find("ID_88117422")!;

        document.SetText(node, "a & b < c > \"d\" — ü");

        // Act and assert, step by step.
        var written = document.ToText();
        Assert.Contains("TEXT=\"a &amp; b &lt; c &gt; &quot;d&quot; — ü\"", written, StringComparison.Ordinal);
        Assert.Equal("a & b < c > \"d\" — ü", MindmapDocument.Parse(written).Find("ID_88117422")!.Text);
    }
}
