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
        // Requirement 3.3. Without id assignment, because two corpus nodes have none and
        // assigning them is the one change the spec allows on a file ADP did not edit.
        var original = Corpus();

        var written = Load(assignMissingIds: false).ToText();

        Assert.Equal(original, written);
    }

    [Fact]
    public void ParseThenWrite_IsStableAcrossASecondPass()
    {
        var once = Load().ToText();

        var twice = MindmapDocument.Parse(once).ToText();

        Assert.Equal(once, twice);
    }

    [Fact]
    public void ParseThenWrite_KeepsAnElementAndAnAttributeItDoesNotUnderstand()
    {
        // Requirement 3.2: something a future Freeplane adds survives a trip through ADP.
        var original = Corpus()
            .Replace("<node TEXT=\"Backend\"", "<node TEXT=\"Backend\" FUTURE_ATTR=\"kept\"")
            .Replace("<edge COLOR=\"#ff0000\" WIDTH=\"2\"/>", "<edge COLOR=\"#ff0000\" WIDTH=\"2\"/>\n<future_element answer=\"42\"><inner/></future_element>");

        var written = MindmapDocument.Parse(original, assignMissingIds: false).ToText();

        Assert.Equal(original, written);
    }

    [Fact]
    public void AssigningMissingIds_ChangesOnlyTheTwoNodesThatHadNone()
    {
        var document = Load();

        Assert.All(document.Nodes, node => Assert.NotEqual("", node.Id));
        var ribbon = document.Nodes.Single(node => node.Text == "Ribbon");
        Assert.StartsWith("ID_", ribbon.Id, StringComparison.Ordinal);
        Assert.Equal("ID_411002938", ribbon.Parent!.Id); // an id the file already had is untouched
    }

    // ---- reading ------------------------------------------------------------------------------

    [Fact]
    public void Parse_ReadsTheTree()
    {
        var document = Load();

        Assert.Equal("ADP architecture", document.Root.Text);
        Assert.Equal(4, document.Root.Children.Count);
        Assert.Equal(["Backend", "Client", "Diagram types", "Deliberately deep"], document.Root.Children.Select(child => child.Text));
        Assert.Equal(25, document.Nodes.Count());
    }

    [Fact]
    public void Parse_ReadsTextHeldAsRichContent()
    {
        var document = Load();

        var rich = document.Find("ID_411002940")!;

        Assert.Equal("A node whose text is rich content rather than a TEXT attribute", rich.Text);
    }

    [Fact]
    public void Parse_ReadsNotes_WithAndWithoutAContentType()
    {
        var document = Load();

        Assert.StartsWith("The map ADP's round-trip tests read.", document.Root.Notes, StringComparison.Ordinal);
        Assert.Contains("<node> is not a node.", document.Find("ID_88117420")!.Notes, StringComparison.Ordinal);
        Assert.Equal("", document.Find("ID_88117422")!.Notes);
    }

    [Fact]
    public void Parse_ReadsFoldLinkAndEmptyText()
    {
        var document = Load();

        Assert.True(document.Find("ID_88117425")!.Folded);
        Assert.Equal("../../../../backend/EtAlii.Adp.Backend/Context/ContextServiceImpl.cs", document.Find("ID_88117420")!.Link);
        Assert.Null(document.Find("ID_88117422")!.Link);
        Assert.Equal("", document.Find("ID_411002939")!.Text);
    }

    [Fact]
    public void Parse_AMalformedFile_ThrowsNamingTheProblem()
    {
        var exception = Assert.Throws<MindmapFormatException>(() => MindmapDocument.Parse("<map><node TEXT='unclosed'></map>"));

        Assert.Contains("well-formed", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_AFileThatIsNotAMap_Throws()
    {
        Assert.Throws<MindmapFormatException>(() => MindmapDocument.Parse("<html><body/></html>"));
    }

    [Fact]
    public void Parse_AMapWithTwoRoots_Throws()
    {
        Assert.Throws<MindmapFormatException>(() => MindmapDocument.Parse("<map><node TEXT=\"a\"/><node TEXT=\"b\"/></map>"));
    }

    // ---- structural edits ---------------------------------------------------------------------

    [Fact]
    public void AddChild_AppendsAsTheLastChild_AndWritesOnePerLine()
    {
        var document = Load();
        var backend = document.Find("ID_411002937")!;

        var added = document.AddChild(backend, "Logging");

        Assert.Equal("Logging", backend.Children[^1].Text);
        Assert.StartsWith("ID_", added.Id, StringComparison.Ordinal);
        Assert.Contains("<node TEXT=\"Logging\" ID=\"" + added.Id + "\"/>\n</node>", document.ToText(), StringComparison.Ordinal);
    }

    [Fact]
    public void AddSibling_InsertsRightAfterTheNode()
    {
        var document = Load();
        var contextService = document.Find("ID_88117420")!;

        document.AddSibling(contextService, "Projects");

        Assert.Equal(["Context service", "Projects", "Commands & history", "Hierarchy"], contextService.Parent!.Children.Select(child => child.Text));
    }

    [Fact]
    public void Move_CarriesTheWholeSubtree_ToTheGivenParentAndIndex()
    {
        var document = Load();
        var hierarchy = document.Find("ID_88117425")!;
        var client = document.Find("ID_411002938")!;

        document.Move(hierarchy, client, 0);

        Assert.Equal("Hierarchy", client.Children[0].Text);
        Assert.Equal(2, client.Children[0].Children.Count); // HierarchyModel and RootFolderWatcher came along
        Assert.DoesNotContain(document.Find("ID_411002937")!.Children, child => child.Id == "ID_88117425");
    }

    [Fact]
    public void Move_IntoItsOwnSubtree_IsRefused()
    {
        var document = Load();
        var backend = document.Find("ID_411002937")!;
        var grandchild = document.Find("ID_88117422")!;

        Assert.Throws<InvalidOperationException>(() => document.Move(backend, grandchild, 0));
    }

    [Fact]
    public void Move_TheRoot_IsRefused()
    {
        var document = Load();

        Assert.Throws<InvalidOperationException>(() => document.Move(document.Root, document.Find("ID_411002937")!, 0));
    }

    [Fact]
    public void RemoveThenRestore_PutsTheSubtreeBackExactly()
    {
        var document = Load();
        var before = document.ToText();
        var hierarchy = document.Find("ID_88117425")!;
        var parent = hierarchy.Parent!;
        var index = hierarchy.IndexInParent;

        var subtree = document.Remove(hierarchy);
        Assert.Null(document.Find("ID_88117425"));
        Assert.Null(document.Find("ID_88117428")); // a grandchild went with it
        document.Restore(subtree, parent, index);

        Assert.Equal(before, document.ToText());
    }

    [Fact]
    public void Remove_TheRoot_IsRefused()
    {
        var document = Load();

        Assert.Throws<InvalidOperationException>(() => document.Remove(document.Root));
    }

    // ---- content edits -------------------------------------------------------------------------

    [Fact]
    public void SetText_OnARichContentNode_ReplacesTheMarkupWithPlainText()
    {
        var document = Load();
        var rich = document.Find("ID_411002940")!;

        document.SetText(rich, "plain now");

        Assert.Equal("plain now", rich.Text);
        Assert.DoesNotContain("rich content", document.ToText(), StringComparison.Ordinal);
    }

    [Fact]
    public void SetText_ToEmpty_IsKept()
    {
        // Requirement 7.6: a node is not required to have text.
        var document = Load();
        var node = document.Find("ID_88117422")!;

        document.SetText(node, "");

        Assert.Equal("", node.Text);
        Assert.Contains("TEXT=\"\" ID=\"ID_88117422\"", document.ToText(), StringComparison.Ordinal);
    }

    [Fact]
    public void SetNotes_AddsThenUpdatesThenRemovesTheNote()
    {
        var document = Load();
        var node = document.Find("ID_88117422")!;

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
        var document = Load();
        var node = document.Find("ID_88117422")!;

        document.SetLink(node, "../file.cs");
        Assert.Equal("../file.cs", node.Link);

        document.SetLink(node, null);
        Assert.Null(node.Link);
    }

    [Fact]
    public void Edits_EscapeWhatXmlRequires_AndNothingElse()
    {
        var document = Load();
        var node = document.Find("ID_88117422")!;

        document.SetText(node, "a & b < c > \"d\" — ü");

        var written = document.ToText();
        Assert.Contains("TEXT=\"a &amp; b &lt; c &gt; &quot;d&quot; — ü\"", written, StringComparison.Ordinal);
        Assert.Equal("a & b < c > \"d\" — ü", MindmapDocument.Parse(written).Find("ID_88117422")!.Text);
    }
}
