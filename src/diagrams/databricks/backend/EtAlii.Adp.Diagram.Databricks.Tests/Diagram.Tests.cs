using Xunit;

namespace EtAlii.Adp.Diagram.Databricks.Tests;

/// <summary>
/// The family's three definitions: what discovery reads (databricks-diagrams Requirement 1.1).
/// </summary>
public class DiagramTests
{
    [Fact]
    public void ThreeDefinitions_WithTheCataloguedOrigins()
    {
        // Arrange & act & assert.
        Assert.Equal(
            new[] { "databricks/bundle", "databricks/job", "databricks/pipeline" },
            Diagram.Definitions.Select(definition => definition.Origin.Key));
    }

    [Fact]
    public void EveryExtension_IsShared_SoNoBareFileIsEverClaimed()
    {
        // Arrange & act & assert.
        // Assert, first, that the family declares anything at all. Every claim below is
        // vacuously true of an empty Definitions array, so without this the guard would go
        // quiet precisely when the module had stopped registering its diagram types.
        Assert.True(
            Diagram.Definitions.Length > 0,
            "Diagram.Definitions is empty, so this guard checked nothing; the family declares three definitions.");

        // .yml and .json belong to the whole world; a file becomes one of these diagrams when
        // the user registers it (Requirement 1.2).
        Assert.All(Diagram.Definitions, definition =>
        {
            if (definition == null!)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            Assert.True(definition.SharedExtension);
            Assert.False(definition.RoutesBareBody);
            Assert.True(definition.HasDocumentSibling);
        });
    }

    [Fact]
    public void EveryDefinition_CarriesItsIconAndDescription()
    {
        // Arrange & act & assert.
        Assert.Equal("mdi-package-variant-closed", Diagram.Bundle.Icon);
        Assert.Equal("mdi-transit-connection-horizontal", Diagram.Job.Icon);
        Assert.Equal("mdi-pipe", Diagram.Pipeline.Icon);
        // Assert, first, that the family declares anything at all. Every claim below is
        // vacuously true of an empty Definitions array, so without this the guard would go
        // quiet precisely when the module had stopped registering its diagram types.
        Assert.True(
            Diagram.Definitions.Length > 0,
            "Diagram.Definitions is empty, so this guard checked nothing; the family declares three definitions.");

        Assert.All(Diagram.Definitions, definition => Assert.NotEqual("", definition.Description));
    }
}
