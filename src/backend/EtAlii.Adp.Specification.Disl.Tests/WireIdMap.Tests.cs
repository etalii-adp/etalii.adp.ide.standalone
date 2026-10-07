using Xunit;

namespace EtAlii.Adp.Specification.Disl.Tests;

/// <summary>The host's wire ids, read from a definition's <c>x-&lt;prefix&gt;</c> block.</summary>
public class WireIdMapTests
{
    private static readonly Dictionary<string, object?> NoBindings = [];

    [Fact]
    public void ATool_HasItsPaletteIdAndItsDrop()
    {
        var ids = Derivations.HypeCycleIds;

        Assert.Equal("ghg.toolbox.trend", ids.ToolId("trend"));
        Assert.Equal("ghg.add.note", ids.DropActionId("note"));
    }

    [Theory]
    [InlineData("Trend", "editLabel", null, "ghg.rename")]
    [InlineData("Trend", "delete", null, "ghg.remove")]
    [InlineData("Influence", "delete", null, "ghg.disconnect")]
    [InlineData("Trend", "operation", "evenPhases", "ghg.even-phases")]
    [InlineData("diagram", "operation", "addTriggerHere", "ghg.add.trigger")]
    [InlineData("connection", "connect", null, "ghg.connect.influence")]
    public void AnAction_IsKeyedByOperationOrKind_AndATypeOverridesIt(string type, string kind, string? operation, string expected) =>
        Assert.Equal(expected, Derivations.HypeCycleIds.ActionId([type], kind, operation, NoBindings));

    [Fact]
    public void ANameInAnId_IsTheBindingMappedThroughTypes()
    {
        var bindings = new Dictionary<string, object?> { ["kind"] = "RepeatUntil" };

        var id = Derivations.BehaviorModelIds.ActionId(["Sequence", "Composite", "BehaviorNode"], "operation", "addChild", bindings);

        Assert.Equal("abm.add.repeat", id);
    }

    [Theory]
    [InlineData("peakInfluences", "ghg.peak-influences")]
    [InlineData("troughEnd", "ghg.trough-end")]
    [InlineData("From attachment", "ghg.from-attachment")]
    [InlineData("unmapped", "unmapped")]
    public void AProperty_IsKeyedByItsItemsKey(string key, string expected) => Assert.Equal(expected, Derivations.HypeCycleIds.PropertyId(key));

    [Fact]
    public void WithoutABlock_EveryNameIsItsOwnId()
    {
        var ids = WireIdMap.Of(Derivations.HypeCycle, "x-none");

        Assert.Same(WireIdMap.None, ids);
        Assert.Equal("trend", ids.ToolId("trend"));
        Assert.Null(ids.DropActionId("trend"));
        Assert.Equal("evenPhases", ids.ActionId(["Trend"], "operation", "evenPhases", NoBindings));
        Assert.Equal("delete", ids.ActionId(["Influence"], "delete", null, NoBindings));
    }
}
