using System.Text;
using EtAlii.Adp.Specification.Fbl;
using EtAlii.Adp.Specification.Fbl.Planning;
using EtAlii.Adp.Specification.Fbl.Plugins;
using Xunit;

namespace EtAlii.Adp.Diagram.Mindmap.Tests;

/// <summary>
/// The persistence plugin of <c>mindmap.fbl#mindmap</c> reads a Freeplane body into the elements the
/// DISL model is built from: one per node, in document order, each under its parent, with an id of
/// its own even where the file gives two nodes one <c>ID</c> or a node none.
/// </summary>
public class MindmapFreeplanePluginTests
{
    private readonly MindmapFreeplanePlugin _plugin = new();

    private PluginReadResult Read(string text) =>
        _plugin.Read(new PluginReadRequest([new PluginFile("", Encoding.UTF8.GetBytes(text))]));

    [Fact]
    public void ANode_IsReadWithItsTextNotesLinkFoldAndSide_UnderItsParent()
    {
        // Arrange.
        const string text = "<map version=\"freeplane 1.12.15\">\n<node TEXT=\"root\" ID=\"ID_1\">\n" +
            "<node TEXT=\"left\" ID=\"ID_2\" POSITION=\"left\" FOLDED=\"true\" LINK=\"notes.md\"><richcontent TYPE=\"NOTE\"><html><head/><body><p>A note</p></body></html></richcontent>\n<node TEXT=\"deep\" ID=\"ID_3\"/>\n</node>\n" +
            "</node>\n</map>\n";

        // Act.
        var result = Read(text);

        // Assert.
        Assert.False(result.Unreadable);
        Assert.Empty(result.Findings);
        Assert.Equal(["ID_1", "ID_2", "ID_3"], result.Elements.Select(element => element.Id));
        var left = result.Elements[1];
        Assert.True(left.IdIsStored);
        Assert.Equal("ID_1", left.ParentId);
        Assert.Equal("children", left.ParentSlot);
        Assert.Equal("left", left.Attributes["text"]);
        Assert.Equal("A note", left.Attributes["notes"]);
        Assert.Equal("notes.md", left.Attributes["link"]);
        Assert.Equal(true, left.Attributes["folded"]);
        Assert.Equal("left", left.Attributes["position"]);
        Assert.Equal("ID_2", left.Attributes[MindmapFreeplanePlugin.StoredIdAttribute]);
        Assert.Equal(3, left.Line);
        Assert.Null(result.Elements[0].ParentId);
    }

    [Fact]
    public void ALink_IsReadOnlyWhenTheFileHasOne_EvenAnEmptyOne()
    {
        // Arrange.
        const string text = "<map version=\"freeplane 1.12.15\"><node TEXT=\"root\" ID=\"ID_1\" LINK=\"\"><node TEXT=\"plain\" ID=\"ID_2\"/></node></map>";

        // Act.
        var result = Read(text);

        // Assert.
        Assert.Equal("", result.Elements[0].Attributes["link"]);
        Assert.False(result.Elements[1].Attributes.ContainsKey("link"));
        Assert.False(result.Elements[1].Attributes.ContainsKey("position"));
    }

    [Fact]
    public void ALaterHolderOfAnId_AndANodeWithoutOne_GetIdsOfTheirOwn_AndKeepWhatTheFileWrites()
    {
        // Arrange.
        const string text = "<map version=\"freeplane 1.12.15\"><node TEXT=\"root\" ID=\"ID_1\">" +
            "<node TEXT=\"a\" ID=\"ID_A\"><node TEXT=\"under the first\" ID=\"ID_X\"/></node><node TEXT=\"a again\" ID=\"ID_A\"><node TEXT=\"under the second\" ID=\"ID_Y\"/></node>" +
            "<node TEXT=\"none\"/></node></map>";

        // Act.
        var elements = Read(text).Elements;

        // Assert.
        Assert.Equal(elements.Count, elements.Select(element => element.Id).Distinct().Count());
        Assert.Equal("ID_A", elements[1].Id);
        Assert.True(elements[1].IdIsStored);
        Assert.NotEqual("ID_A", elements[3].Id);
        Assert.False(elements[3].IdIsStored);
        Assert.Equal("ID_A", elements[3].Attributes[MindmapFreeplanePlugin.StoredIdAttribute]);
        Assert.Equal(elements[3].Id, elements[4].ParentId);
        Assert.False(elements[5].IdIsStored);
        Assert.Equal("", elements[5].Attributes[MindmapFreeplanePlugin.StoredIdAttribute]);
    }

    [Fact]
    public void ABodyThatIsNotAMap_IsUnreadable_WithTheReaderSReason()
    {
        // Act.
        var result = Read("<map version=\"freeplane 1.12.15\">\n<node TEXT=\"one\" ID=\"ID_1\"/>\n<node TEXT=\"two\" ID=\"ID_2\"/>\n</map>\n");

        // Assert.
        Assert.True(result.Unreadable);
        Assert.Empty(result.Elements);
        var finding = Assert.Single(result.Findings);
        Assert.Equal(FindingCodes.Unparseable, finding.Code);
        Assert.Equal("A map has exactly one root node; this one has 2.", finding.Message);
    }

    [Fact]
    public void TheBinding_NamesThisPluginAsItsReader()
    {
        // Act.
        var binding = MindmapDefinition.Binding;

        // Assert.
        Assert.Equal(MindmapFreeplanePlugin.PluginId, binding.Plugin!.Plugin);
        Assert.Equal(MindmapFreeplanePlugin.PluginId, _plugin.Id);
    }

    [Fact]
    public void AChange_IsRefused_BecauseTheCommandsEditTheXmlInPlace()
    {
        // Arrange.
        var body = PluginBody.Open("<map version=\"freeplane 1.12.15\"><node TEXT=\"root\" ID=\"ID_1\"/></map>"u8.ToArray(), MindmapDefinition.Binding, _plugin, "map.mm");

        // Act.
        var result = body.Plan(new ModelChange.Remove("ID_1"));

        // Assert.
        Assert.IsType<PlanResult.Refused>(result);
    }
}
