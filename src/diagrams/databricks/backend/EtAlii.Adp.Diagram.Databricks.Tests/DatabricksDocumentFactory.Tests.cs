using EtAlii.Adp.Documents;
using Xunit;

namespace EtAlii.Adp.Diagram.Databricks.Tests;

/// <summary>
/// The minimal documents a new diagram starts as: complete enough to open, parse and validate
/// clean - a skeleton that opens with findings would be a refusal factory.
/// </summary>
public class DatabricksDocumentFactoryTests
{
    [Theory]
    [InlineData("databricks", "bundle")]
    [InlineData("databricks", "job")]
    [InlineData("databricks", "pipeline")]
    public async Task EachTypesMinimalDocument_ParsesAndValidatesClean(string vendor, string type)
    {
        // Arrange.
        var factory = new DatabricksDocumentFactory(new DiagramOrigin(vendor, type));

        // Act.
        var text = factory.CreateEmptyDocument("Nightly Ingest");
        var validator = new DatabricksValidator(factory.Origin);
        var problems = await validator.ValidateAsync(
            new DiagramValidationRequest(text, "Nightly Ingest", "", "", null),
            TestContext.Current.CancellationToken);

        // Assert.
        Assert.Empty(problems);
    }

    [Fact]
    public void TheJobSkeleton_CarriesAStarterTask_NamedFromTheBase()
    {
        // Arrange.
        var factory = new DatabricksDocumentFactory(new DiagramOrigin("databricks", "job"));

        // Act.
        var document = LineDocument.Parse(factory.CreateEmptyDocument("Nightly Ingest"));
        var job = Assert.Single(JobParser.Parse(DatabricksYaml.Root(document), document));

        // Assert.
        Assert.Equal("nightly_ingest", job.Key);
        Assert.Equal("Nightly Ingest", job.Name);
        Assert.Single(job.Tasks);
    }

    [Fact]
    public void ThePipelineSkeleton_IsJson_WithOneLibrary()
    {
        // Arrange.
        var factory = new DatabricksDocumentFactory(new DiagramOrigin("databricks", "pipeline"));

        // Act.
        var document = LineDocument.Parse(factory.CreateEmptyDocument("bronze"));
        var pipeline = Assert.Single(PipelineParser.Parse(DatabricksYaml.Root(document), document));

        // Assert.
        Assert.True(DatabricksSplices.IsJson(document));
        Assert.Equal("bronze", pipeline.Name);
        Assert.Single(pipeline.Libraries);
    }
}
