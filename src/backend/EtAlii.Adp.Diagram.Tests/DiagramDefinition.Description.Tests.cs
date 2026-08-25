using Xunit;

namespace EtAlii.Adp.Diagram.Tests;

/// <summary>
/// Every discovered type describes itself, because the Add dialog shows that description when
/// a user selects the type. A module shipping without one leaves a blank panel, which is worse
/// than no panel at all.
/// </summary>
public class DiagramDefinitionDescriptionTests
{
    [Fact]
    public void EveryDiscoveredDefinition_CarriesADescription()
    {
        // Arrange.
        var definitions = DiagramDefinition.All;

        // Act.
        var undescribed = definitions.Where(d => string.IsNullOrWhiteSpace(d.Description)).ToArray();

        // Assert.
        Assert.Empty(undescribed);
    }

    [Fact]
    public void ADescription_SaysMoreThanItsTitle()
    {
        // Arrange.
        var definitions = DiagramDefinition.All;

        // Act: a description that merely repeats the title tells a user nothing new.
        var echoes = definitions
            .Where(d => string.Equals(d.Description.Trim(), d.Title.Trim(), StringComparison.OrdinalIgnoreCase))
            .ToArray();

        // Assert.
        Assert.Empty(echoes);
    }
}
