using EtAlii.Adp.Documents;
using Xunit;

namespace EtAlii.Adp.Diagram.Databricks.Tests;

/// <summary>
/// The minimal documents a new diagram starts as: complete enough to open, parse and validate
/// clean - a skeleton that opens with findings would be a refusal factory.
/// </summary>
public class DatabricksEmptyDocumentTests
{
    [Theory]
    [InlineData("databricks", "bundle")]
    [InlineData("databricks", "job")]
    [InlineData("databricks", "pipeline")]
    public async Task EachTypesMinimalDocument_ParsesAndValidatesClean(string vendor, string type)
    {
        // Arrange.
        var origin = new ToolOrigin(vendor, type);

        // Act.
        var text = ServiceCollectionAddDatabricksExtension.EmptyDocument(origin.Type, "Nightly Ingest");
        var validator = new DatabricksValidator(origin);
        var problems = await validator.ValidateAsync(
            new DiagramValidationRequest(text, "Nightly Ingest", default, "", null),
            TestContext.Current.CancellationToken);

        // Assert.
        Assert.Empty(problems);
    }

    [Fact]
    public void TheJobSkeleton_CarriesAStarterTask_NamedFromTheBase()
    {
        // Act.
        var document = LineDocument.Parse(ServiceCollectionAddDatabricksExtension.EmptyDocument("job", "Nightly Ingest"));
        var job = Assert.Single(DatabricksModel.Read(document).Jobs);

        // Assert.
        Assert.Equal("nightly_ingest", job.Key);
        Assert.Equal("Nightly Ingest", job.Name);
        Assert.Single(job.Tasks);
    }

    [Fact]
    public void ThePipelineSkeleton_IsJson_WithOneLibrary()
    {
        // Act.
        var document = LineDocument.Parse(ServiceCollectionAddDatabricksExtension.EmptyDocument("pipeline", "bronze"));
        var pipeline = Assert.Single(DatabricksModel.Read(document).Pipelines);

        // Assert.
        Assert.StartsWith("{", document.Text.TrimStart(), StringComparison.Ordinal);
        Assert.Equal("bronze", pipeline.Name);
        Assert.Single(pipeline.Libraries);
    }
}
