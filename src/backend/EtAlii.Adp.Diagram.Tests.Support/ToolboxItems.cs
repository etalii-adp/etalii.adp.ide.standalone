using Xunit;

namespace EtAlii.Adp.Diagram.Tests.Support;

/// <summary>What a test holds every toolbox entry of a diagram type to.</summary>
public static class ToolboxItems
{
    /// <summary>Fails unless <paramref name="item"/> carries all of its data: an id, a label, an icon, a description and the action a drop runs.</summary>
    public static void AssertComplete(ToolboxItemDefinition item)
    {
        ArgumentNullException.ThrowIfNull(item);
        Assert.NotEmpty(item.Id);
        Assert.NotEmpty(item.Label);
        Assert.NotEmpty(item.Icon);
        Assert.NotEmpty(item.Description);
        Assert.NotEmpty(item.DropActionId);
    }
}
