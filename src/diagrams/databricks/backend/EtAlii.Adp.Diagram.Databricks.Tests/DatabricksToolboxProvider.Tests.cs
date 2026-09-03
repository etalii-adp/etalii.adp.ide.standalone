using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EtAlii.Adp.Diagram.Databricks.Tests;

/// <summary>
/// The palettes: per-type entries whose drops name the same add actions the menus run
/// (databricks-diagrams Requirement 9).
/// </summary>
public class DatabricksToolboxProviderTests
{
    [Fact]
    public void EachType_OffersItsOwnPalette_NamingRealActionIds()
    {
        // Arrange.
        var provider = new ServiceCollection().AddDatabricks().BuildServiceProvider();

        // Act.
        var toolboxes = provider.GetServices<IDiagramToolboxProvider>()
            .Where(toolbox => toolbox.Origin.Vendor == "databricks")
            .ToDictionary(toolbox => toolbox.Origin.Type);

        // Assert.
        Assert.Equal(
            new[] { "databricks.add-task:notebook", "databricks.add-task:python", "databricks.add-task:condition" },
            toolboxes["job"].Items.Select(item => item.DropActionId));
        Assert.Equal(
            new[] { "databricks.add-library:notebook", "databricks.add-library:file" },
            toolboxes["pipeline"].Items.Select(item => item.DropActionId));
        Assert.Equal(
            new[] { "databricks.add-resource:jobs", "databricks.add-resource:pipelines" },
            toolboxes["bundle"].Items.Select(item => item.DropActionId));
    }
}
