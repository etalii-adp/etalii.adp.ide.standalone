using Xunit;

namespace EtAlii.Adp.Designer.Tests;

/// <summary>What a designer definition guarantees about itself, whatever a module typed.</summary>
public class DesignerDefinitionTests
{
    [Fact]
    public void Formats_WhenNoneAreDeclared_IsEmptyRatherThanNull()
    {
        // Arrange and act.
        var definition = new DesignerDefinition("fixture/form", "Fixture form");

        // Assert.
        Assert.Empty(definition.Formats);
    }

    [Fact]
    public void Formats_KeepTheOrderTheyWereDeclaredIn()
    {
        // Arrange and act: the order is the order the formats are offered in.
        var definition = new DesignerDefinition(
            "fixture/form",
            "Fixture form",
            Formats: [new("YAML", ".yaml"), new("JSON", ".json"), new("XML", ".xml")]);

        // Assert.
        Assert.Equal(new[] { "YAML", "JSON", "XML" }, definition.Formats.Select(format => format.Title));
    }

    [Fact]
    public void AFormatsExtension_IsLowerCased_HoweverItWasTyped()
    {
        // Arrange and act.
        var format = new DesignerFormat("YAML", ".YAML");

        // Assert.
        Assert.Equal(".yaml", format.Extension);
    }
}
