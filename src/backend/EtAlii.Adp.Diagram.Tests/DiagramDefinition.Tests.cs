using Xunit;

namespace EtAlii.Adp.Diagram.Tests;

public class DiagramDefinitionTests
{
    [Fact]
    public void Constructor_KeepsTheOriginAndTitleItWasGiven()
    {
        var origin = new DiagramOrigin("uml", "class");

        var definition = new DiagramDefinition(origin, "Class diagram");

        Assert.Same(origin, definition.Origin);
        Assert.Equal("Class diagram", definition.Title);
    }
}
