using Xunit;

namespace EtAlii.Adp.Diagram.Mindmap.Tests;

public class MindmapToolboxProviderTests
{
    [Fact]
    public void TheToolboxOffersOneNodeEntry()
    {
        // Arrange.
        var provider = new MindmapToolboxProvider();

        // Act.
        var items = provider.Items;

        // Assert.
        var item = Assert.Single(items);
        Assert.Equal("Node", item.Label);
        Assert.NotEmpty(item.Icon);
        Assert.NotEmpty(item.Description);
    }

    [Fact]
    public void DroppingTheNodeEntryRunsTheAddChildAction()
    {
        // Arrange: the drop must share the add-child action's implementation - command,
        // prompt, undo - rather than carry one of its own, so the id must be that action's.
        var provider = new MindmapToolboxProvider();

        // Act.
        var item = Assert.Single(provider.Items);

        // Assert.
        Assert.Equal(MindmapContextActionProvider.AddChildActionId, item.DropActionId);
    }

    [Fact]
    public void TheProviderSpeaksForTheMindmapOrigin()
    {
        // Arrange.
        var provider = new MindmapToolboxProvider();

        // Act & Assert: the shared diagram service routes DescribeToolbox by origin.
        Assert.Equal(Diagram.Definition.Origin, provider.Origin);
    }
}
