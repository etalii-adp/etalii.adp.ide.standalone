using EtAlii.Adp.Diagram;
using Xunit;

namespace EtAlii.Adp.Diagram.AzurePipeline.Tests;

public class DiagramTests
{
    [Fact]
    public void TheModule_DeclaresOnePipelineType()
    {
        // Arrange & act.
        var definition = Assert.Single(Diagram.Definitions);

        // Assert.
        Assert.Equal("azure-devops", definition.Origin.Vendor);
        Assert.Equal("pipeline", definition.Origin.Type);
        Assert.NotEmpty(definition.Description);
    }

    [Fact]
    public void ItsExtension_IsSharedAndSoNeverRoutesABareBody()
    {
        // Arrange.
        var definition = Assert.Single(Diagram.Definitions);

        // Act & assert: .yml belongs to no one type, so a file becomes a pipeline only when the
        // user registers it (Requirements 2.1-2.2).
        Assert.Equal(".yml", definition.Extension);
        Assert.True(definition.SharedExtension);
        Assert.False(definition.RoutesBareBody);
    }
}
