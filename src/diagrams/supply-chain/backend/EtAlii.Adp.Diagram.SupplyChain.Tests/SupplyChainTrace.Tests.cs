using EtAlii.Adp.Documents;
using Xunit;

namespace EtAlii.Adp.Diagram.SupplyChain.Tests;

/// <summary>The trace: upstream and downstream from the selection, the rest dimmed.</summary>
public sealed class SupplyChainTraceTests
{
    private static SupplyChainLayout Automotive() =>
        SupplyChainLayout.Of(SupplyChainParser.Parse(LineDocument.Parse(File.ReadAllText(SupplyChainExamples.Automotive))));

    [Fact]
    public void NothingSelected_MarksNothing()
    {
        // Act.
        var trace = SupplyChainTrace.Through(Automotive(), null);

        // Assert.
        Assert.False(trace.IsActive);
        Assert.Equal("", trace.Of("vehicle-plant"));
    }

    [Fact]
    public void ANode_LightsWhatSuppliesIt_AndWhatItSupplies_AndDimsTheRest()
    {
        // Act.
        var trace = SupplyChainTrace.Through(Automotive(), "pack-assembly");

        // Assert.
        Assert.Equal(SupplyChainTrace.Selected, trace.Of("pack-assembly"));
        Assert.Equal(SupplyChainTrace.Upstream, trace.Of("kolwezi-cobalt"));
        Assert.Equal(SupplyChainTrace.Upstream, trace.Of("cell-gigafactory-to-pack-assembly"));
        Assert.Equal(SupplyChainTrace.Downstream, trace.Of("vehicle-plant"));
        Assert.Equal(SupplyChainTrace.Downstream, trace.Of("private-buyers"));

        // Off the chain both ways: tyres go into the same car, but not through the battery pack.
        Assert.Equal(SupplyChainTrace.Dimmed, trace.Of("thai-rubber"));
        Assert.Equal(SupplyChainTrace.Dimmed, trace.Of("tyre-plant-to-vehicle-plant"));
    }

    [Fact]
    public void AFlow_TracesUpFromItsSupplier_AndDownFromItsConsumer()
    {
        // Act.
        var trace = SupplyChainTrace.Through(Automotive(), "steel-mill-to-body-shop");

        // Assert.
        Assert.Equal(SupplyChainTrace.Selected, trace.Of("steel-mill-to-body-shop"));
        Assert.Equal(SupplyChainTrace.Upstream, trace.Of("steel-mill"));
        Assert.Equal(SupplyChainTrace.Upstream, trace.Of("pilbara-iron"));
        Assert.Equal(SupplyChainTrace.Downstream, trace.Of("body-shop"));
        Assert.Equal(SupplyChainTrace.Downstream, trace.Of("company-fleets"));
        Assert.Equal(SupplyChainTrace.Dimmed, trace.Of("atacama-lithium"));
    }

    [Fact]
    public void AGroup_TracesFromAllItsMembers_AndAGroupOnTheChainIsRelated()
    {
        // Act.
        var trace = SupplyChainTrace.Through(Automotive(), "central-africa");

        // Assert.
        Assert.Equal(SupplyChainTrace.Selected, trace.Of("central-africa"));
        Assert.Equal(SupplyChainTrace.Selected, trace.Of("kolwezi-cobalt"));
        Assert.Equal(SupplyChainTrace.Downstream, trace.Of("cathode-plant"));
        Assert.Equal(SupplyChainTrace.Related, trace.Of("china"));
        Assert.Equal(SupplyChainTrace.Dimmed, trace.Of("australia"));
    }

    [Fact]
    public void AnIdThatNamesNothing_MarksNothing()
    {
        // Act.
        var trace = SupplyChainTrace.Through(Automotive(), "nowhere");

        // Assert.
        Assert.False(trace.IsActive);
    }
}
