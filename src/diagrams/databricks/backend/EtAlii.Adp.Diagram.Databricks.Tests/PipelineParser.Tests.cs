using EtAlii.Adp.Documents;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.Databricks.Tests;

/// <summary>
/// The pipeline reading - including that a settings .json comes through the same YAML door as
/// everything else (databricks-diagrams Requirements 2.1 and 5).
/// </summary>
public class PipelineParserTests
{
    private static IReadOnlyList<PipelineModel> Parse(string name)
    {
        var document = LineDocument.Parse(
            File.ReadAllText(IoPath.Combine(AppContext.BaseDirectory, "Fixtures", name)));
        return PipelineParser.Parse(DatabricksYaml.Root(document), document);
    }

    [Fact]
    public void ABareSettingsJson_IsOnePipeline()
    {
        // Arrange & act.
        var pipeline = Assert.Single(Parse("pipeline.json"));

        // Assert.
        Assert.Equal("", pipeline.Key);
        Assert.Equal("bronze_to_gold", pipeline.Name);
        Assert.Equal("lakehouse_dev", pipeline.Catalog);
        Assert.Equal("gold", pipeline.Schema);
        Assert.True(pipeline.Serverless);
        Assert.False(pipeline.Continuous);
        Assert.True(pipeline.Development);
        Assert.Equal("CURRENT", pipeline.Channel);
    }

    [Fact]
    public void Libraries_CarryTheirKindsAndPaths()
    {
        // Arrange & act.
        var pipeline = Assert.Single(Parse("pipeline.json"));

        // Assert.
        Assert.Equal(3, pipeline.Libraries.Count);
        Assert.Equal(
            new[] { ("notebook", "transformations/bronze"), ("file", "transformations/silver.py"), ("glob", "transformations/gold/**") },
            pipeline.Libraries.Select(library => (library.Kind, library.Path)));
    }

    [Fact]
    public void Notifications_CarryRecipientsAndAlerts()
    {
        // Arrange & act.
        var pipeline = Assert.Single(Parse("pipeline.json"));

        // Assert.
        var notification = Assert.Single(pipeline.Notifications);
        Assert.Equal("data-eng@example.com", Assert.Single(notification.Recipients));
        Assert.Equal("on-update-failure", Assert.Single(notification.Alerts));
    }

    [Fact]
    public void AnUnmodelledKey_LandsAsAnUnknownNode()
    {
        // Arrange & act.
        var pipeline = Assert.Single(Parse("pipeline.json"));

        // Assert.
        // `edition` is real configuration this module does not model - present in the model,
        // never written (Requirement 2.4).
        var unknown = Assert.Single(pipeline.UnknownNodes);
        Assert.Equal("edition", unknown.Key);
    }

    [Fact]
    public void AKeyedDeclarationInABundle_ReadsWithItsKey()
    {
        // Arrange & act.
        var pipeline = Assert.Single(Parse("bundle.yml"));

        // Assert.
        Assert.Equal("bronze_to_gold", pipeline.Key);
        Assert.Equal("Bronze to gold", pipeline.Name);
    }

    [Fact]
    public void AFileWithNoPipelineInIt_ReadsAsNone()
    {
        // Arrange & act & assert.
        Assert.Empty(Parse("job.yml"));
    }
}
