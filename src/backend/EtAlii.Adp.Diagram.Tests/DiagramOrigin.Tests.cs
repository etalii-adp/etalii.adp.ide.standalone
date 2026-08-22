using Xunit;

namespace EtAlii.Adp.Diagram.Tests;

public class DiagramOriginTests
{
    [Fact]
    public void Constructor_WithoutASubtype_DefaultsSubtypeToEmpty()
    {
        var origin = new DiagramOrigin("uml", "class");

        Assert.Equal("uml", origin.Vendor);
        Assert.Equal("class", origin.Type);
        Assert.Equal("", origin.Subtype);
    }

    [Fact]
    public void Constructor_WithASubtype_KeepsIt()
    {
        var origin = new DiagramOrigin("c4", "component", "code");

        Assert.Equal("code", origin.Subtype);
    }

    [Fact]
    public void Equality_ForTwoOriginsWithTheSameValues_HoldsByValue()
    {
        Assert.Equal(new DiagramOrigin("uml", "class"), new DiagramOrigin("uml", "class"));
    }
}
